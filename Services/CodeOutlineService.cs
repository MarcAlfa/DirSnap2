using System.Text.RegularExpressions;

namespace DirSnap2.Services;

/// <summary>One entry of the code outline panel (function, class, procedure, heading...).</summary>
public sealed class OutlineItem
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;   // shown in the tooltip
    public int Line { get; init; }                      // 0-based line index
    public int Level { get; init; }                     // nesting depth (indent in the panel)

    public string Glyph => Kind switch
    {
        "class" or "type" or "package" or "package body" or "type body"
            or "struct" or "interface" or "enum" or "record" => "", // box
        "heading" => "",                                                         // text
        "trigger" => "",                                                         // lightning
        "view" => "",                                                            // grid
        _ => "",                                                                 // code
    };

    public string LineText => (Line + 1).ToString();
    public Microsoft.UI.Xaml.Thickness Indent => new(Level * 12, 0, 0, 0);
    public string ToolTip => $"{Kind} {Name} — line {Line + 1}";
}

/// <summary>
/// Lightweight, regex-based outline (function/class names) for Python, JavaScript/TypeScript,
/// C#, PowerShell, batch labels, PL/SQL and Markdown headings. It is not a real parser: it skips
/// comments and Python docstrings line by line, which is enough to list definitions.
/// </summary>
public static class CodeOutlineService
{
    private enum Lang { None, Python, JavaScript, CSharp, PowerShell, Batch, PlSql, Markdown }

    private static Lang GetLang(string? path)
    {
        var ext = System.IO.Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".py" or ".pyw" => Lang.Python,
            ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" or ".mts" or ".cts" => Lang.JavaScript,
            ".cs" => Lang.CSharp,
            ".ps1" or ".psm1" => Lang.PowerShell,
            ".bat" or ".cmd" => Lang.Batch,
            ".sql" or ".pls" or ".plsql" or ".pks" or ".pkb" or ".pck" or ".prc" or ".fnc"
                or ".trg" or ".tps" or ".tpb" or ".vw" or ".ddl" => Lang.PlSql,
            ".md" or ".markdown" => Lang.Markdown,
            _ => Lang.None,
        };
    }

    private static bool IsPlainText(string? path) =>
        System.IO.Path.GetExtension(path ?? string.Empty).Equals(".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the file may have an outline: a known code extension, or a .txt file
    /// (PL/SQL is often saved as .txt: its content decides, see <see cref="Parse"/>).
    /// </summary>
    public static bool MayBeSupported(string? path) => GetLang(path) != Lang.None || IsPlainText(path);

    public static bool IsMarkdown(string? path) => GetLang(path) == Lang.Markdown;

    // A real PL/SQL definition: "CREATE [OR REPLACE] PACKAGE|PROCEDURE|FUNCTION|TRIGGER|TYPE ...",
    // or "PROCEDURE x ... IS/AS" / "FUNCTION x ... RETURN" at the start of a line
    private static readonly Regex PlSqlSniff = new(
        @"^\s*(?:CREATE\s+(?:OR\s+REPLACE\s+)?(?:(?:NON)?EDITIONABLE\s+)?(?:PACKAGE|PROCEDURE|FUNCTION|TRIGGER|TYPE)\s+[""\w$#.]+" +
        @"|(?:PROCEDURE|FUNCTION)\s+[""\w$#.]+\s*(?:\([^)]*\)?)?.*?\b(?:IS|AS|RETURN)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromMilliseconds(500));

    private const int SniffLength = 256 * 1024;

    private static bool LooksLikePlSql(string text)
    {
        try
        {
            var sample = text.Length > SniffLength ? text[..SniffLength] : text;
            return PlSqlSniff.IsMatch(sample.Replace("\r\n", "\n").Replace('\r', '\n'));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Outline of the file, or null when the file has no supported language
    /// (for example a .txt that does not contain PL/SQL).
    /// </summary>
    public static List<OutlineItem>? Parse(string? path, string? text)
    {
        var lang = GetLang(path);
        if (lang == Lang.None && IsPlainText(path) && !string.IsNullOrEmpty(text) && LooksLikePlSql(text))
            lang = Lang.PlSql;
        if (lang == Lang.None) return null;

        var result = new List<OutlineItem>();
        if (string.IsNullOrEmpty(text)) return result;

        // TextBox text uses lone CR: split on any line ending so line numbers match the editor
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        try
        {
            switch (lang)
            {
                case Lang.Python: ParsePython(lines, result); break;
                case Lang.JavaScript: ParseJavaScript(lines, result); break;
                case Lang.CSharp: ParseCSharp(lines, result); break;
                case Lang.PowerShell: ParsePowerShell(lines, result); break;
                case Lang.Batch: ParseBatch(lines, result); break;
                case Lang.PlSql: ParsePlSql(lines, result); break;
                case Lang.Markdown: ParseMarkdown(lines, result); break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological line: keep what was found so far
        }
        return result;
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    // ==================== PYTHON ====================

    private static readonly Regex PyDef = new(@"^(?<ind>[ \t]*)(?:async[ \t]+)?(?<kw>def|class)[ \t]+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled, Timeout);

    private static void ParsePython(string[] lines, List<OutlineItem> result)
    {
        // Indentation stack -> nesting level
        var indents = new List<int>();
        string? openQuote = null; // inside a """ or ''' string

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (openQuote != null)
            {
                if (CountOccurrences(line, openQuote) % 2 == 1) openQuote = null;
                continue;
            }

            var m = PyDef.Match(line);
            if (m.Success)
            {
                int indent = IndentWidth(m.Groups["ind"].Value);
                while (indents.Count > 0 && indents[^1] >= indent) indents.RemoveAt(indents.Count - 1);
                result.Add(new OutlineItem
                {
                    Name = m.Groups["name"].Value,
                    Kind = m.Groups["kw"].Value == "class" ? "class" : "def",
                    Line = i,
                    Level = indents.Count,
                });
                indents.Add(indent);
            }

            // Multi-line string opened on this line (odd number of triple quotes)
            var stripped = StripPyComment(line);
            foreach (var q in new[] { "\"\"\"", "'''" })
            {
                if (CountOccurrences(stripped, q) % 2 == 1) { openQuote = q; break; }
            }
        }
    }

    private static string StripPyComment(string line)
    {
        int hash = line.IndexOf('#');
        // Good enough: a '#' inside a string on a def line is rare
        return hash >= 0 && line.IndexOf("\"\"\"", StringComparison.Ordinal) < 0 && line.IndexOf("'''", StringComparison.Ordinal) < 0
            ? line[..hash]
            : line;
    }

    // ==================== JAVASCRIPT / TYPESCRIPT ====================

    private static readonly Regex JsFunction = new(
        @"^\s*(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:async\s+)?function\s*\*?\s*(?<name>[A-Za-z_$][\w$]*)",
        RegexOptions.Compiled, Timeout);

    private static readonly Regex JsClass = new(
        @"^\s*(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:abstract\s+)?(?<kw>class|interface|enum)\s+(?<name>[A-Za-z_$][\w$]*)",
        RegexOptions.Compiled, Timeout);

    // const foo = function / async (...) => / x => / async function
    private static readonly Regex JsArrow = new(
        @"^\s*(?:export\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::[^=]*)?=>|[A-Za-z_$][\w$]*\s*=>)",
        RegexOptions.Compiled, Timeout);

    // Class method: "  async foo(a, b) {", "static get bar() {", "private baz(): T {"
    private static readonly Regex JsMethod = new(
        @"^\s+(?:(?:public|private|protected|static|async|override|readonly|get|set)\s+)*\*?(?<name>#?[A-Za-z_$][\w$]*)\s*(?:<[^>]*>)?\s*\([^;]*\)\s*(?::[^{;]+)?\{\s*$",
        RegexOptions.Compiled, Timeout);

    // Object/class property: "foo: function(", "foo: async (x) =>", "foo = () =>"
    private static readonly Regex JsProperty = new(
        @"^\s+(?:(?:public|private|protected|static|readonly)\s+)*(?<name>[A-Za-z_$][\w$]*)\s*[:=]\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::[^=]*)?=>|[A-Za-z_$][\w$]*\s*=>)",
        RegexOptions.Compiled, Timeout);

    private static readonly HashSet<string> JsKeywords = new(StringComparer.Ordinal)
    {
        "if", "for", "while", "switch", "catch", "function", "return", "with", "else", "do", "try",
        "super", "new", "typeof", "await", "yield",
    };

    private static void ParseJavaScript(string[] lines, List<OutlineItem> result)
    {
        bool inBlockComment = false;
        int depth = 0;                         // brace depth at the start of the current line
        var classDepths = new Stack<int>();    // brace depth inside each open class

        for (int i = 0; i < lines.Length; i++)
        {
            var code = StripCComments(lines[i], ref inBlockComment, "//");
            while (classDepths.Count > 0 && depth < classDepths.Peek()) classDepths.Pop();
            int level = classDepths.Count;

            Match m;
            if ((m = JsClass.Match(code)).Success)
            {
                result.Add(new OutlineItem { Name = m.Groups["name"].Value, Kind = m.Groups["kw"].Value, Line = i, Level = level });
                classDepths.Push(depth + 1);
            }
            else if ((m = JsFunction.Match(code)).Success || (m = JsArrow.Match(code)).Success)
            {
                result.Add(new OutlineItem { Name = m.Groups["name"].Value, Kind = "function", Line = i, Level = level });
            }
            else if (level > 0 && (m = JsMethod.Match(code)).Success && !JsKeywords.Contains(m.Groups["name"].Value))
            {
                result.Add(new OutlineItem { Name = m.Groups["name"].Value, Kind = "method", Line = i, Level = level });
            }
            else if ((m = JsProperty.Match(code)).Success && !JsKeywords.Contains(m.Groups["name"].Value))
            {
                result.Add(new OutlineItem { Name = m.Groups["name"].Value, Kind = "method", Line = i, Level = level });
            }

            depth += CountBraces(code);
            if (depth < 0) depth = 0;
        }
    }

    // ==================== C# ====================

    private const string CsModifiers =
        @"(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|extern|unsafe|new|partial|readonly|ref|file|required)\s+)*";

    private static readonly Regex CsType = new(
        @"^\s*(?:\[[^\]]*\]\s*)*" + CsModifiers + @"(?<kw>class|struct|interface|enum|record)(?:\s+(?:class|struct))?\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled, Timeout);

    // Method: "public async Task<int> Foo<T>(..." ; constructor: "public Foo(..."
    private static readonly Regex CsMethod = new(
        @"^\s*" + CsModifiers + @"(?:(?<ret>\([^)]*\)|[A-Za-z_][\w.]*(?:<.*?>)?[?\[\],]*)\s+)?(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\(",
        RegexOptions.Compiled, Timeout);

    private static readonly HashSet<string> CsKeywords = new(StringComparer.Ordinal)
    {
        "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "new", "await",
        "throw", "else", "yield", "case", "goto", "nameof", "typeof", "sizeof", "default", "when", "base", "this",
        "fixed", "checked", "unchecked", "var", "in", "is", "not", "and", "or",
    };

    private sealed class CsTypeScope
    {
        public int Depth;    // brace depth of the type's body
        public bool Opened;  // its "{" was reached (it may be on the next line)
        public string Name = string.Empty;
    }

    private static void ParseCSharp(string[] lines, List<OutlineItem> result)
    {
        bool inBlockComment = false;
        int depth = 0;
        var types = new List<CsTypeScope>();

        for (int i = 0; i < lines.Length; i++)
        {
            var code = StripCComments(lines[i], ref inBlockComment, "//");

            // Close types whose body ended; drop a pending type that never opened ("record R(int A);")
            while (types.Count > 0 && types[^1].Opened && depth < types[^1].Depth) types.RemoveAt(types.Count - 1);
            if (types.Count > 0 && !types[^1].Opened && depth >= types[^1].Depth) types[^1].Opened = true;

            int opened = types.Count(t => t.Opened);
            var inner = types.Count > 0 ? types[^1] : null;
            bool inTypeBody = inner is { Opened: true } && depth == inner.Depth;

            Match m;
            if ((m = CsType.Match(code)).Success)
            {
                result.Add(new OutlineItem { Name = m.Groups["name"].Value, Kind = m.Groups["kw"].Value, Line = i, Level = opened });
                types.Add(new CsTypeScope { Depth = depth + 1, Name = m.Groups["name"].Value });
            }
            else if (inTypeBody && (m = CsMethod.Match(code)).Success)
            {
                var name = m.Groups["name"].Value;
                bool hasRet = m.Groups["ret"].Success;
                bool isCtor = !hasRet && inner != null && name == inner.Name;
                if ((hasRet || isCtor) && !CsKeywords.Contains(name) && !CsKeywords.Contains(m.Groups["ret"].Value))
                    result.Add(new OutlineItem { Name = name, Kind = isCtor ? "constructor" : "method", Line = i, Level = opened });
            }

            depth += CountBraces(code);
            if (depth < 0) depth = 0;

            if (types.Count > 0 && !types[^1].Opened)
            {
                if (depth >= types[^1].Depth) types[^1].Opened = true;
                else if (code.Contains(';')) types.RemoveAt(types.Count - 1);
            }
        }
    }

    // ==================== POWERSHELL ====================

    private static readonly Regex PsDef = new(
        @"^\s*(?<kw>function|filter|workflow|class|enum)\s+(?<name>[\w\-:.]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    private static void ParsePowerShell(string[] lines, List<OutlineItem> result)
    {
        bool inBlockComment = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (inBlockComment)
            {
                int end = line.IndexOf("#>", StringComparison.Ordinal);
                if (end < 0) continue;
                inBlockComment = false;
                line = line[(end + 2)..];
            }
            int start = line.IndexOf("<#", StringComparison.Ordinal);
            if (start >= 0 && line.IndexOf("#>", start + 2, StringComparison.Ordinal) < 0)
            {
                inBlockComment = true;
                line = line[..start];
            }

            var m = PsDef.Match(line);
            if (m.Success)
            {
                var kw = m.Groups["kw"].Value.ToLowerInvariant();
                result.Add(new OutlineItem { Name = m.Groups["name"].Value, Kind = kw == "filter" || kw == "workflow" ? "function" : kw, Line = i });
            }
        }
    }

    // ==================== BATCH (.bat / .cmd) ====================

    // ":label" at the start of a line ("::" is a comment)
    private static readonly Regex BatLabel = new(@"^\s*:(?<name>[^\s:+=;,][^\s+=;,]*)", RegexOptions.Compiled, Timeout);

    private static void ParseBatch(string[] lines, List<OutlineItem> result)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var m = BatLabel.Match(lines[i]);
            if (m.Success && !m.Groups["name"].Value.Equals("eof", StringComparison.OrdinalIgnoreCase))
                result.Add(new OutlineItem { Name = m.Groups["name"].Value, Kind = "label", Line = i });
        }
    }

    // ==================== PL/SQL ====================

    private static readonly Regex SqlDef = new(
        @"^\s*(?:CREATE\s+(?:OR\s+REPLACE\s+)?(?:(?:NON)?EDITIONABLE\s+)?(?:FORCE\s+)?)?(?<kw>PACKAGE\s+BODY|PACKAGE|TYPE\s+BODY|PROCEDURE|FUNCTION|TRIGGER|VIEW|MATERIALIZED\s+VIEW|TYPE)\s+(?<name>(?:""[^""]+""|[A-Za-z_][\w$#]*)(?:\.(?:""[^""]+""|[A-Za-z_][\w$#]*))?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    private static readonly Regex SqlCreate = new(@"^\s*CREATE\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    // Block keywords: BEGIN and CASE open, END closes ("END IF" / "END LOOP" do not: IF and LOOP are not counted; "END CASE" closes its CASE)
    private static readonly Regex SqlBlockWord = new(@"\b(?<w>BEGIN|CASE|END)\b(?:\s+(?<next>IF|LOOP|CASE)\b)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    // What ends a subprogram header: ";" (declaration only) or IS/AS (a body follows)
    private static readonly Regex SqlHeaderEnd = new(@";|\b(?:IS|AS)\b(?<ext>\s+(?:LANGUAGE|EXTERNAL)\b)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout);

    /// <summary>A procedure/function body still open (to hide the subprograms declared inside it).</summary>
    private sealed class SqlScope
    {
        public int Depth;    // block depth when its header was read
        public bool Begun;   // its own BEGIN was reached
    }

    private static void ParsePlSql(string[] lines, List<OutlineItem> result)
    {
        bool inBlockComment = false;
        bool insidePackage = false;
        int depth = 0;
        var scopes = new Stack<SqlScope>();
        bool headerPending = false;   // a procedure/function header still waiting for IS/AS or ";"

        for (int i = 0; i < lines.Length; i++)
        {
            var code = StripCComments(lines[i], ref inBlockComment, "--");
            if (code.Trim() == "/")
            {
                // End of a SQL*Plus statement: start clean
                depth = 0; scopes.Clear(); headerPending = false; insidePackage = false;
                continue;
            }

            var m = SqlDef.Match(code);
            int scanFrom = 0;
            if (m.Success)
            {
                var kw = Regex.Replace(m.Groups["kw"].Value.ToLowerInvariant(), @"\s+", " ");
                bool isCreate = SqlCreate.IsMatch(code);
                if (isCreate)
                {
                    depth = 0; scopes.Clear(); headerPending = false;
                }

                bool isSubprogram = kw is "procedure" or "function";
                // Declared inside another procedure/function: not listed
                bool nested = isSubprogram && scopes.Count > 0;

                // "TYPE t IS TABLE OF ..." inside a package is a local type, not worth listing
                bool skip = nested || (kw == "type" && insidePackage && !isCreate);

                if (!skip)
                {
                    bool isContainer = kw is "package" or "package body" or "type body";
                    int level;
                    if (isContainer || isCreate)
                    {
                        insidePackage = isContainer;
                        level = 0;
                    }
                    else
                    {
                        level = insidePackage ? 1 : 0;
                    }
                    result.Add(new OutlineItem { Name = m.Groups["name"].Value.Replace("\"", ""), Kind = kw, Line = i, Level = level });
                }

                if (isSubprogram)
                {
                    headerPending = true;
                    scanFrom = m.Index + m.Length;
                }
            }

            // Header spanning several lines: IS/AS opens a body, ";" was only a declaration
            if (headerPending)
            {
                var h = SqlHeaderEnd.Match(code, scanFrom);
                if (h.Success)
                {
                    headerPending = false;
                    if (h.Value != ";" && !h.Groups["ext"].Success)
                        scopes.Push(new SqlScope { Depth = depth });
                    scanFrom = h.Index + h.Length;
                }
                else
                {
                    continue;
                }
            }

            foreach (Match w in SqlBlockWord.Matches(code, scanFrom))
            {
                var word = w.Groups["w"].Value.ToUpperInvariant();
                if (word == "BEGIN")
                {
                    if (scopes.Count > 0 && !scopes.Peek().Begun && depth == scopes.Peek().Depth)
                        scopes.Peek().Begun = true;
                    depth++;
                }
                else if (word == "CASE")
                {
                    depth++;
                }
                else if (!w.Groups["next"].Success
                         || w.Groups["next"].Value.Equals("CASE", StringComparison.OrdinalIgnoreCase)) // END, END CASE, END name
                {
                    depth = Math.Max(0, depth - 1);
                    if (scopes.Count > 0 && scopes.Peek().Begun && depth == scopes.Peek().Depth)
                        scopes.Pop();
                }
            }
        }
    }

    // ==================== MARKDOWN ====================

    private static readonly Regex MdHeading = new(@"^(?<h>#{1,6})\s+(?<name>.+?)\s*#*\s*$", RegexOptions.Compiled, Timeout);

    private static void ParseMarkdown(string[] lines, List<OutlineItem> result)
    {
        bool inFence = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```")) { inFence = !inFence; continue; }
            if (inFence) continue;

            var m = MdHeading.Match(line);
            if (m.Success)
            {
                result.Add(new OutlineItem
                {
                    Name = m.Groups["name"].Value,
                    Kind = "heading",
                    Line = i,
                    Level = m.Groups["h"].Value.Length - 1,
                });
            }
        }
    }

    // ==================== HELPERS ====================

    /// <summary>Removes /* */ block comments (tracked across lines), line comments and string contents.</summary>
    private static string StripCComments(string line, ref bool inBlockComment, string lineComment)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        char quote = '\0';
        for (int j = 0; j < line.Length; j++)
        {
            char c = line[j];
            char next = j + 1 < line.Length ? line[j + 1] : '\0';

            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; j++; }
                continue;
            }
            if (quote != '\0')
            {
                if (c == '\\' && lineComment == "//") { j++; continue; }
                if (c == quote) { quote = '\0'; sb.Append(c); }
                continue;
            }
            if (c == '/' && next == '*') { inBlockComment = true; j++; continue; }
            if (string.CompareOrdinal(line, j, lineComment, 0, lineComment.Length) == 0) break;
            if (c == '\'' || c == '"' || (c == '`' && lineComment == "//"))
            {
                quote = c;
                sb.Append(c);
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static int CountBraces(string code)
    {
        int n = 0;
        foreach (var c in code)
        {
            if (c == '{') n++;
            else if (c == '}') n--;
        }
        return n;
    }

    private static int CountOccurrences(string s, string token)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf(token, idx, StringComparison.Ordinal)) >= 0) { count++; idx += token.Length; }
        return count;
    }

    private static int IndentWidth(string ws)
    {
        int w = 0;
        foreach (var c in ws) w += c == '\t' ? 4 : 1;
        return w;
    }
}
