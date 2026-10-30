using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using sts2MultiplayerSpectatorModule.Scripts;

int assertions = 0;
void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new Exception(description);
}

string token = new('a', 32);
var packet = new SpectatorPacket(2, "chat", token, "中文弹幕", 12, "观众", "dca747");
byte[] encoded = SpectatorWireProtocol.Encode(packet);
byte[] legacy = JsonSerializer.SerializeToUtf8Bytes(new
{
    Version = 2, Kind = "chat", Token = token, Text = "中文弹幕", Sequence = 12L, Name = "观众", Color = "dca747"
}, SpectatorWireProtocol.Json);
Check(encoded.SequenceEqual(legacy), "The wire format must remain byte-compatible with version 2.");
Check(SpectatorWireProtocol.Decode(encoded) == packet, "Chinese text must round-trip.");
Check(SpectatorWireProtocol.Decode([]) == null, "Empty messages must be rejected.");
Check(SpectatorWireProtocol.Decode(new byte[24001]) == null, "Oversized messages must be rejected before parsing.");
foreach (var invalid in new[] { packet with { Version = 1 }, packet with { Token = "old" },
    packet with { Name = new string('n', 81) }, packet with { Text = null! } })
    Check(SpectatorWireProtocol.Decode(SpectatorWireProtocol.Encode(invalid)) == null, "Invalid envelopes must be rejected.");
// Limits apply to UTF-8 bytes, not the number of characters.
Check(SpectatorWireProtocol.Decode(SpectatorWireProtocol.Encode(packet with { Text = new string('中', 9000) })) == null,
    "Multibyte text must respect the wire byte limit.");
bool malformedRejected = false;
try { SpectatorWireProtocol.Decode(Encoding.UTF8.GetBytes("{")); }
catch (JsonException) { malformedRejected = true; }
Check(malformedRejected, "Malformed JSON must remain available for caller logging.");

var stream = new ActivityMessageStream();
var notices = new List<ActivityNotice>();
stream.Received += notices.Add;
var notice = new ActivityNotice("玩家", "获得了", "格挡药剂", "edc87d");
var first = stream.Create(token, notice)!;
var second = stream.Create(token, notice with { Text = "遗物" })!;
Check(first.Sequence == 1 && second.Sequence == 2, "Outgoing notice IDs must increase.");
stream.Receive(second);
stream.Receive(first);
stream.Receive(second);
Check(notices.Count == 1 && notices[0].Text == "遗物", "Duplicate and older notices must not be delivered.");
stream.Receive(first with { Sequence = 3, Text = "{" });
stream.Receive(first with { Sequence = 3 });
Check(notices.Count == 2, "Malformed payloads must not consume a valid sequence number.");
stream.Receive(first with { Token = new string('b', 32) });
Check(notices.Count == 3, "A new authenticated session must reset receive ordering.");
foreach (var invalid in new[] { notice with { Name = "bad\nname" }, notice with { Text = new string('t', 501) },
    notice with { Action = "unknown" }, notice with { Color = "zzzzzz" } })
    Check(stream.Create(token, invalid) == null, "Invalid notices must not be published.");
stream.Receive(first with { Token = new string('b', 32), Sequence = 2, Text = new string('x', 4001) });
Check(notices.Count == 3, "Oversized notice payloads must not be delivered.");
stream.Receive(first with { Token = new string('b', 32), Sequence = 2, Kind = "chat" });
Check(notices.Count == 3, "Other message kinds must not enter the notice stream.");
foreach (string action in new[] { "gold", "upgrade", "enchant" })
{
    var receiver = new ActivityMessageStream();
    ActivityNotice? received = null;
    receiver.Received += value => received = value;
    var change = notice with { Action = action, Text = action == "gold" ? "25" : "Strike+ · Sharp" };
    receiver.Receive(stream.Create(token, change)!);
    Check(received == change, "Card changes and gold gains must survive the authenticated notice stream.");
}

var assembler = new BattleAssembler();
var frame = new BattleFrame("begin", token, 0, 10, 0, 2, Convert.ToBase64String([1, 2]));
Check(assembler.Accept(frame with { Part = 1, Data = Convert.ToBase64String([3, 4]) }, 0) == null,
    "Out-of-order fragments must wait for completion.");
Check(assembler.Accept(frame with { Part = 1, Data = Convert.ToBase64String([3, 4]) }, 1) == null,
    "Duplicate fragments must not complete a message.");
Check(assembler.Accept(frame, 2)!.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Fragments must assemble in part order.");
assembler.Accept(frame, 3);
Check(assembler.Accept(frame with { Combat = new string('c', 32), Part = 1 }, 4) == null,
    "Different combat IDs must never share fragments.");
assembler.Expire(15004);
Check(assembler.Accept(frame with { Part = 1 }, 15005) == null, "Expired incomplete messages must be discarded.");
assembler.Clear();
Check(assembler.Accept(frame with { Parts = 176 }, 0) == null, "Excessive fragment counts must be rejected.");
Check(assembler.Accept(frame with { Part = 2 }, 0) == null, "Invalid fragment indices must be rejected.");
Check(assembler.Accept(frame with { Data = Convert.ToBase64String(new byte[12001]) }, 0) == null,
    "Oversized fragments must be rejected.");

// The shared sender preserves empty messages, exact boundaries, and out-of-order assembly.
foreach (int size in new[] { 0, 1, BattleAssembler.PartBytes, BattleAssembler.PartBytes + 1, 256000 })
{
    var bytes = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
    var parts = BattleFrame.Split("event-view", token, 7, 10, bytes).ToArray();
    Check(parts.Length == Math.Max(1, (size + BattleAssembler.PartBytes - 1) / BattleAssembler.PartBytes), "Shared fragmentation must preserve boundary sizes.");
    var receiver = new BattleAssembler();
    byte[]? joined = null;
    foreach (var part in parts.Reverse()) joined = receiver.Accept(part, 10) ?? joined;
    Check(joined != null && joined.SequenceEqual(bytes), "Shared fragments must restore the exact payload in any order.");
    Check(parts.All(part => SpectatorWireProtocol.Encode(new(2, "battle", token, JsonSerializer.Serialize(part))).Length <= SpectatorWireProtocol.MaxBytes),
        "Every fragment must fit the outer spectator packet.");
}
Check(!BattleFrame.Split("begin", token, 0, 0, new byte[BattleAssembler.MaxBytes + 1]).Any(), "Oversized outgoing snapshots must not send fragments.");

// Translation resources share keys and placeholders, and network text uses the receiver's language.
string localizationRoot = Path.Combine(AppContext.BaseDirectory, "localization");
var english = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(localizationRoot,"eng","spectator.json")))!;
var chinese = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(localizationRoot,"zhs","spectator.json")))!;
Check(english.Keys.Order().SequenceEqual(chinese.Keys.Order()), "English and Chinese must have matching localization keys.");
foreach(var key in english.Keys)
{
    var pattern = @"\{[0-9]+\}";
    Check(System.Text.RegularExpressions.Regex.Matches(english[key],pattern).Select(m=>m.Value).Order().SequenceEqual(
        System.Text.RegularExpressions.Regex.Matches(chinese[key],pattern).Select(m=>m.Value).Order()), "Translations must retain placeholders: "+key);
}
var catalog = new LocalizationCatalog(localizationRoot);
Check(catalog.Text("zhs","menu.spectate")=="观战" && catalog.Text("eng","menu.spectate")=="Spectate", "Select the requested language.");
Check(catalog.Text("zzz","menu.spectate")=="Spectate", "Missing language must fall back to English.");
Check(catalog.Text("../zhs","menu.spectate")=="Spectate", "Language paths cannot escape localization directories.");
Check(catalog.Text("eng","unknown.key")=="[unknown.key]", "Unknown keys have a readable fallback.");
string localizedWire=L.Wire("local.port",33772);
L.Language=()=>"eng";
Check(L.Resolve(localizedWire)=="Local spectator port: 33772", "Wire text resolves in the receiver's language.");
L.Language=()=>"zhs";
Check(L.Resolve(localizedWire)=="本地观战端口：33772", "The same wire text supports another receiver language.");
Check(L.Resolve("plain player text")=="plain player text", "Native text and player messages remain unchanged.");
Check(L.Resolve("[[spectator:====]]")=="[[spectator:====]]", "Malformed localization envelopes do not break message rendering.");
Check(L.Resolve(L.Wire("events.choice_prefix")+"测试选项")=="选择：测试选项", "Translated prefixes can accompany native event titles.");
string fixture=Path.Combine(AppContext.BaseDirectory,"localization-fixture");
Directory.CreateDirectory(Path.Combine(fixture,"eng")); Directory.CreateDirectory(Path.Combine(fixture,"fra"));
File.WriteAllText(Path.Combine(fixture,"eng","spectator.json"),"{\"format\":\"Hello {0}\",\"fallback\":\"Fallback\"}");
File.WriteAllText(Path.Combine(fixture,"fra","spectator.json"),"{\"format\":\"Broken {\"}");
var fallbackCatalog=new LocalizationCatalog(fixture);
Check(fallbackCatalog.Text("fra","fallback")=="Fallback", "Missing translated keys use English.");
Check(fallbackCatalog.Text("fra","format","Alex")=="Hello Alex", "Malformed translated format strings use English.");
File.WriteAllText(Path.Combine(fixture,"fra","spectator.json"),"invalid JSON");
Check(new LocalizationCatalog(fixture).Text("fra","fallback")=="Fallback", "Malformed language files use English.");
L.Language=()=>"eng";
Check(stream.Create(token,notice with{Action="obtain"})!=null && stream.Create(token,notice with{Action="choice"})!=null,
    "Activity actions use language-independent identifiers.");

var inventoryParts=BattleFrame.Split("inventory",token,1,0,new byte[24001]).ToArray();
var inventoryJoin=new BattleAssembler(); byte[]? inventoryBytes=null;
foreach(var part in inventoryParts.Reverse()) inventoryBytes=inventoryJoin.Accept(part,0)??inventoryBytes;
Check(inventoryBytes?.Length==24001,"Inventory snapshots use bounded fragmentation without the single-packet size limit.");
var embeddedCatalog = new LocalizationCatalog("no-loose-assets", embedded: true);
Check(embeddedCatalog.Text("zhs", "menu.spectate") == "观战" && embeddedCatalog.Text("eng", "menu.spectate") == "Spectate",
    "Embedded translations work without external resource directories.");
Check(embeddedCatalog.Text("missing", "menu.spectate") == "Spectate", "Embedded translations retain English fallback.");
// Every language shown in the game's selector has a complete embedded translation table.
string[] supportedLanguages = ["zhs","eng","fra","ita","deu","spa","jpn","kor","pol","ptb","rus","esp","tha","tur"];
foreach (string language in supportedLanguages)
{
    var path = Path.Combine(localizationRoot,language,"spectator.json");
    Check(File.Exists(path), "Game language has a translation file: "+language);
    var table = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))!;
    Check(table.Keys.Order().SequenceEqual(english.Keys.Order()), "Language has all keys: "+language);
    foreach (var key in english.Keys)
    {
        Check(!string.IsNullOrWhiteSpace(table[key]), "Translation is nonempty: "+language+"/"+key);
        string Tokens(string value,string pattern) => string.Join("|",System.Text.RegularExpressions.Regex.Matches(value,pattern).Select(m=>m.Value).Order());
        Check(Tokens(table[key],@"\{[0-9]+\}")==Tokens(english[key],@"\{[0-9]+\}"), "Translation retains format placeholders: "+language+"/"+key);
        Check(Tokens(table[key],@"\[[^\]]+\]")==Tokens(english[key],@"\[[^\]]+\]"), "Translation retains BBCode: "+language+"/"+key);
        Check(embeddedCatalog.Text(language,key,"A","B","C")==string.Format(table[key],"A","B","C"),
            "Embedded translation matches the source table: "+language+"/"+key);
    }
    L.Language=()=>language;
    Check(L.Resolve(L.Wire("local.port",33772))==string.Format(table["local.port"],33772), "Network notices use receiver language: "+language);
}
L.Language=()=>"eng";

// Inspect metadata without loading Godot or the game into the test process.
if (args.Length > 0)
{
    using var file = File.OpenRead(args[0]);
    using var pe = new PEReader(file);
    var metadata = pe.GetMetadataReader();
    bool hasLocalTools = metadata.TypeDefinitions.Any(handle =>
        metadata.GetString(metadata.GetTypeDefinition(handle).Namespace).Contains("LocalTesting"));
    Check(hasLocalTools == args.Contains("--expect-local-tools"), "Build must honor IncludeLocalTools.");
}
Console.WriteLine($"Passed {assertions} protocol, ordering, fragment and build-boundary checks.");
