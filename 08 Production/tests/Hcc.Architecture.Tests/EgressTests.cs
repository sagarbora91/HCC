using System.Text.RegularExpressions;

namespace Hcc.Architecture.Tests;

/// <summary>
/// EGRESS-CONTRACT §2: every <c>*.cs</c> under <c>08 Production/src</c> is scanned (regex over the source with comments and string literals
/// removed). Outside <c>Hcc.Infrastructure.Egress</c> nothing may construct or inject an <c>HttpClient</c>, create a client of another name than
/// "egress", or use <c>WebRequest</c> / <c>WebClient</c> / <c>HttpWebRequest</c> / <c>SmtpClient</c> / sockets / <c>Dns</c>. Test projects are
/// outside <c>src</c> and therefore allowed. Second rule: <c>AddHttpClient</c> appears exactly once, for "egress", with the gate as its first handler.
/// </summary>
public sealed partial class EgressTests
{
    /// <summary>
    /// Reviewed exceptions: (relative path, rule) → reason. On 2 Nov 2026 HCC makes no outbound call (the Key Agent is reached
    /// by the agent polling the server, not the other way round). A future LAN Key Agent client may be added here only if it asserts an
    /// RFC1918 / loopback host at startup.
    /// </summary>
    private static readonly Dictionary<(string Path, string Rule), string> AllowList = new()
    {
        // Reviewed 2 Nov 2026 (F5-E1): only the AddressFamily enum (IPv4 / IPv6 bucket of a sign-in client address); no socket is opened.
        [("Hcc.Application/Access/SignInThrottle.cs", "Socket")] = "AddressFamily only (client IP bucketing), no connection",
    };

    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("new HttpClient", NewHttpClient()),
        ("HttpClient injection", InjectedHttpClient()),
        ("CreateClient other than egress", CreateClient()),
        ("WebRequest", WebRequestUse()),
        ("WebClient", Word("WebClient")),
        ("HttpWebRequest", Word("HttpWebRequest")),
        ("SmtpClient", Word("SmtpClient")),
        ("Socket", SocketUse()),
        ("TcpClient", Word("TcpClient")),
        ("UdpClient", Word("UdpClient")),
        ("Dns", DnsUse()),
    ];

    private static Regex Word(string w) => new(@"(?<![\w])" + w + @"(?![\w])", RegexOptions.CultureInvariant);

    [GeneratedRegex(@"new\s+(System\.Net\.Http\.)?HttpClient\s*\(")]
    private static partial Regex NewHttpClient();

    [GeneratedRegex(@"[(,]\s*(System\.Net\.Http\.)?HttpClient\??\s+\w+\s*[,)=]")]
    private static partial Regex InjectedHttpClient();

    [GeneratedRegex(@"CreateClient\s*\((?!\s*(""egress""|EgressKeys\.ClientName)\s*\))")]
    private static partial Regex CreateClient();

    [GeneratedRegex(@"(?<![\w])(System\.Net\.)?WebRequest\s*\.|new\s+(System\.Net\.)?WebRequest\b|(?<![\w])FileWebRequest(?![\w])|(?<![\w])FtpWebRequest(?![\w])")]
    private static partial Regex WebRequestUse();

    [GeneratedRegex(@"System\.Net\.Sockets|new\s+Socket\s*\(|(?<![\w])Socket\s+\w+\s*[=;,)]")]
    private static partial Regex SocketUse();

    [GeneratedRegex(@"(?<![\w.])(System\.Net\.)?Dns\s*\.\s*(Get|Begin|Resolve)")]
    private static partial Regex DnsUse();

    [GeneratedRegex("\"\"\"[\\s\\S]*?\"\"\"")]
    private static partial Regex RawStrings();

    [GeneratedRegex(@"@""(?:[^""]|"""")*""|\$?""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)'")]
    private static partial Regex Strings();

    [GeneratedRegex(@"/\*[\s\S]*?\*/|//[^\n]*")]
    private static partial Regex Comments();

    [GeneratedRegex(@"AddHttpClient\s*\(")]
    private static partial Regex AddHttpClient();

    /// <summary>08 Production (walked up from the test output: the build path map hides the source path).</summary>
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Hcc.slnx")) && Directory.Exists(Path.Combine(d.FullName, "src"))) return d.FullName;
        throw new DirectoryNotFoundException("08 Production (Hcc.slnx) not found above " + AppContext.BaseDirectory);
    }

    private static string SrcDir => Path.Combine(Root(), "src");

    /// <summary>The source with comments and string literals blanked (string contents are not code); the egress client name is kept.</summary>
    public static string Code(string source)
    {
        var s = source.Replace("\"egress\"", "EgressKeys.ClientName", StringComparison.Ordinal);
        s = RawStrings().Replace(s, "\"\"");
        s = Comments().Replace(s, " ");
        return Strings().Replace(s, "\"\"");
    }

    public static IEnumerable<(string Rule, int Line)> Violations(string source)
    {
        var code = Code(source);
        foreach (var (rule, rx) in Rules)
        {
            foreach (Match m in rx.Matches(code)) yield return (rule, code[..m.Index].Count(c => c == '\n') + 1);
        }
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SrcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static bool InEgress(string file) => Path.GetRelativePath(SrcDir, file).Replace('\\', '/').StartsWith("Hcc.Infrastructure.Egress/", StringComparison.Ordinal);

    [Fact]
    public void No_outbound_network_api_outside_the_egress_project()
    {
        var files = SourceFiles().ToList();
        Assert.True(files.Count > 200, "src scan found too few files: " + SrcDir);
        var found = new List<string>();
        foreach (var f in files.Where(f => !InEgress(f)))
        {
            var rel = Path.GetRelativePath(SrcDir, f).Replace('\\', '/');
            foreach (var (rule, line) in Violations(File.ReadAllText(f)))
                if (!AllowList.ContainsKey((rel, rule))) found.Add($"{rel}:{line} {rule}");
        }

        Assert.True(found.Count == 0, "Outbound network use outside Hcc.Infrastructure.Egress (route it through the \"egress\" client):\n" + string.Join("\n", found));
    }

    [Fact]
    public void The_rules_catch_each_forbidden_use_and_ignore_comments_strings_and_lookalikes()
    {
        string[] bad =
        [
            "var c = new HttpClient();",
            "public sealed class X(HttpClient http) { }",
            "var c = factory.CreateClient(\"ota\");",
            "var c = factory.CreateClient();",
            "var r = WebRequest.Create(u);",
            "using var w = new WebClient();",
            "HttpWebRequest r = null!;",
            "using var s = new SmtpClient(\"h\");",
            "using System.Net.Sockets;",
            "var t = new TcpClient();",
            "var u = new UdpClient();",
            "var a = Dns.GetHostAddresses(\"h\");",
        ];
        foreach (var b in bad) Assert.NotEmpty(Violations(b));
        string[] fine =
        [
            "var c = factory.CreateClient(\"egress\");",
            "var c = factory.CreateClient(EgressKeys.ClientName);",
            "// new HttpClient() is forbidden",
            "var sql = \"\"\"SELECT WebClient FROM Dns.GetHostAddresses\"\"\";",
            "record WebRequestRow(long Id); var x = new ConfirmWebRequestRequest();",
            "var s = \"new TcpClient()\";",
        ];
        foreach (var f in fine) Assert.Empty(Violations(f));
    }

    [Fact]
    public void Egress_client_is_registered_once_with_the_gate_first()
    {
        var hits = SourceFiles().SelectMany(f => AddHttpClient().Matches(Code(File.ReadAllText(f))).Select(m => (File: f, m.Index))).ToList();
        Assert.Single(hits);
        var (file, _) = hits[0];
        Assert.True(InEgress(file), "AddHttpClient must live in Hcc.Infrastructure.Egress: " + file);
        var code = Code(File.ReadAllText(file));
        Assert.Matches(@"AddHttpClient\s*\(\s*EgressKeys\.ClientName\s*\)\s*\.\s*AddHttpMessageHandler\s*<\s*EgressGateHandler\s*>\s*\(\s*\)", code);
    }
}
