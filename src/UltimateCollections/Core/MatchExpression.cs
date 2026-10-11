// MatchExpression.cs
//
// The 'match' key: a boolean expression over selectors, e.g.
//     match  actor:"Tom Hanks" AND (genre:War OR tmdb_keyword:"world war ii") AND NOT rated:R
// Every selector turns into a set of library items, so AND is an intersection, OR a union and NOT "everything in the
// library except". That is what lets TMDb lists, MDBList lists and Emby's own metadata (tags, genres, studios,
// people, years, ratings) be mixed freely in one collection. Several 'match' lines in a section mean ANY of them,
// like every other repeated key.
//
// NOTE: the selectors are checked while the settings are read, so a typo in a field name stops the run with the line
// number, the same as every other mistake in the settings.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UltimateCollections.Core
{
    // What kind of thing a selector looks at; decides where its items come from.
    public enum SelectorKind
    {
        Text,       // compared with a text field of the library item (tag, genre, ...)
        Number,     // compared with a number field (year, rating, ...)
        Type,       // movie or series
        Person,     // Emby's people, looked up in Emby
        Online,     // downloaded list (TMDb, MDBList), cached like the other lists
    }

    // One field:value selector.
    public sealed class MatchAtom
    {
        // Field name -> kind. People map to Emby's person types; actor includes guest stars so it works for shows.
        static readonly Dictionary<string, SelectorKind> Kinds = new Dictionary<string, SelectorKind>
        {
            ["title"] = SelectorKind.Text, ["genre"] = SelectorKind.Text, ["tag"] = SelectorKind.Text,
            ["studio"] = SelectorKind.Text, ["rated"] = SelectorKind.Text,
            ["year"] = SelectorKind.Number, ["rating"] = SelectorKind.Number, ["critic"] = SelectorKind.Number,
            ["type"] = SelectorKind.Type,
            ["actor"] = SelectorKind.Person, ["director"] = SelectorKind.Person, ["writer"] = SelectorKind.Person,
            ["producer"] = SelectorKind.Person, ["person"] = SelectorKind.Person,
            ["tmdb_keyword"] = SelectorKind.Online, ["tmdb_actor"] = SelectorKind.Online, ["mdblist"] = SelectorKind.Online,
        };

        static readonly Dictionary<string, string[]> Roles = new Dictionary<string, string[]>
        {
            ["actor"] = new[] { "Actor", "GuestStar" }, ["director"] = new[] { "Director" }, ["writer"] = new[] { "Writer" },
            ["producer"] = new[] { "Producer" }, ["person"] = null,
        };

        public static IEnumerable<string> FieldNames => Kinds.Keys;

        public string Field { get; }
        public string Value { get; }
        public SelectorKind Kind { get; }

        // Number selectors: the comparison, and its one or two numbers ('..' is a range, both ends included).
        string _op;
        double _a, _b;

        public MatchAtom(string field, string value)
        {
            Field = field.ToLowerInvariant();
            Value = value;
            if (!Kinds.TryGetValue(Field, out var kind))
                throw new FormatException($"unknown selector '{field}:' (known: {string.Join(", ", Kinds.Keys.OrderBy(x => x))})");
            Kind = kind;
            if (Value.Length == 0)
                throw new FormatException($"'{field}:' needs a value");
            if (Kind == SelectorKind.Number)
                ParseNumber();
            if (Kind == SelectorKind.Type && !new[] { "movie", "series", "show", "tv" }.Contains(Value.ToLowerInvariant()))
                throw new FormatException($"type: is movie or series (got '{Value}')");
        }

        // year:1985 is that year; rating:7 and critic:80 mean "at least", like min_rating. Otherwise >=, <=, >, <, or
        // a range a..b.
        void ParseNumber()
        {
            double Num(string s)
            {
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"{Field}: needs a number, e.g. {Field}:>=7 or {Field}:1980..1989 (got '{Value}')");
                return d;
            }
            var v = Value;
            var dots = v.IndexOf("..", StringComparison.Ordinal);
            if (dots > 0)
            {
                _op = "..";
                _a = Num(v.Substring(0, dots));
                _b = Num(v.Substring(dots + 2));
                return;
            }
            foreach (var op in new[] { ">=", "<=", ">", "<", "=" })
                if (v.StartsWith(op, StringComparison.Ordinal))
                {
                    _op = op;
                    _a = Num(v.Substring(op.Length));
                    return;
                }
            _op = Field == "year" ? "=" : ">=";
            _a = Num(v);
        }

        public bool IsOnline => Kind == SelectorKind.Online;

        public bool IsPerson => Kind == SelectorKind.Person;

        // Emby person types for a people selector; null means any role.
        public string[] PersonRoles => IsPerson ? Roles[Field] : null;

        // The selector as written (quotes added back where needed), for the log and as a key.
        public string Text => Field + ":" + (Value.Any(ch => char.IsWhiteSpace(ch) || ch == '(' || ch == ')') ? "\"" + Value + "\"" : Value);

        // Does a library item match? Text, Number and Type selectors only; people and online lists are resolved by
        // the Syncer, since they need Emby or a download.
        public bool Test(LibraryItem i)
        {
            switch (Field)
            {
                case "title": return (i.Name ?? "").IndexOf(Value, StringComparison.OrdinalIgnoreCase) >= 0;
                case "genre": return AnyEquals(i.Genres);
                case "tag": return AnyEquals(i.Tags);
                case "studio": return AnyEquals(i.Studios);
                case "rated": return string.Equals(i.OfficialRating, Value, StringComparison.OrdinalIgnoreCase);
                case "year": return i.Year.HasValue && Compare(i.Year.Value);
                case "rating": return i.Rating.HasValue && Compare(i.Rating.Value);
                case "critic": return i.CriticRating.HasValue && Compare(i.CriticRating.Value);
                case "type": return i.IsSeries == (Value.ToLowerInvariant() != "movie");
                default: throw new InvalidOperationException($"{Text} is not a library selector");
            }
        }

        bool AnyEquals(string[] values) => (values ?? Array.Empty<string>()).Any(v => string.Equals(v, Value, StringComparison.OrdinalIgnoreCase));

        bool Compare(double x)
        {
            switch (_op)
            {
                case "..": return x >= _a && x <= _b;
                case ">=": return x >= _a;
                case "<=": return x <= _a;
                case ">": return x > _a;
                case "<": return x < _a;
                default: return Math.Abs(x - _a) < 1e-9;
            }
        }

        // An online selector's list is downloaded and saved like a collection's list, under a name made of the
        // selector and the owning collection's download settings. Two collections using the same selector with the
        // same settings share one saved list. min_rating / min_votes / max_pages only mean something for TMDb, so
        // mdblist leaves them out.
        public CollectionDefinition ListDefinition(CollectionDefinition owner, int maxPagesDefault)
        {
            var tmdb = Field != "mdblist";
            var tv = owner.WantsTv;
            var pages = owner.Get("max_pages") ?? maxPagesDefault.ToString(CultureInfo.InvariantCulture);
            var values = new Dictionary<string, string>();
            if (tv)
                values["tv"] = "yes";
            if (tmdb)
            {
                values["max_pages"] = pages;
                foreach (var k in Keys.Discover)
                    if (owner.Get(k) != null)
                        values[k] = owner.Get(k);
            }
            var name = new StringBuilder(Field + "=" + Value.ToLowerInvariant());
            foreach (var kv in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                name.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            var def = new CollectionDefinition(name.ToString(), owner.Line);
            def.Lists[Field] = new List<string> { Value };
            foreach (var kv in values)
                def.Values[kv.Key] = kv.Value;
            return def;
        }
    }

    // ---- the expression tree ----

    public abstract class MatchNode
    {
        // The set of library ids the expression stands for. 'atom' gives each selector's items; 'universe' is every
        // item in scope (movies, plus series with 'tv yes'), which NOT subtracts from.
        public abstract HashSet<long> Eval(Func<MatchAtom, HashSet<long>> atom, HashSet<long> universe);

        public abstract IEnumerable<MatchAtom> Atoms();
    }

    public sealed class AtomNode : MatchNode
    {
        public MatchAtom Atom { get; }

        public AtomNode(MatchAtom atom) => Atom = atom;

        public override HashSet<long> Eval(Func<MatchAtom, HashSet<long>> atom, HashSet<long> universe) => new HashSet<long>(atom(Atom));

        public override IEnumerable<MatchAtom> Atoms() => new[] { Atom };

        public override string ToString() => Atom.Text;
    }

    public sealed class NotNode : MatchNode
    {
        readonly MatchNode _inner;

        public NotNode(MatchNode inner) => _inner = inner;

        public override HashSet<long> Eval(Func<MatchAtom, HashSet<long>> atom, HashSet<long> universe)
        {
            var r = new HashSet<long>(universe);
            r.ExceptWith(_inner.Eval(atom, universe));
            return r;
        }

        public override IEnumerable<MatchAtom> Atoms() => _inner.Atoms();

        public override string ToString() => "NOT " + _inner;
    }

    public sealed class AndOrNode : MatchNode
    {
        readonly bool _and;
        readonly List<MatchNode> _parts;

        public AndOrNode(bool and, List<MatchNode> parts)
        {
            _and = and;
            _parts = parts;
        }

        public override HashSet<long> Eval(Func<MatchAtom, HashSet<long>> atom, HashSet<long> universe)
        {
            var r = _parts[0].Eval(atom, universe);
            foreach (var p in _parts.Skip(1))
            {
                if (_and)
                    r.IntersectWith(p.Eval(atom, universe));
                else
                    r.UnionWith(p.Eval(atom, universe));
            }
            return r;
        }

        public override IEnumerable<MatchAtom> Atoms() => _parts.SelectMany(p => p.Atoms());

        public override string ToString() => "(" + string.Join(_and ? " AND " : " OR ", _parts) + ")";
    }

    // ---- parser ----

    // expr := and (OR and)* ; and := not (AND not)* ; not := NOT not | '(' expr ')' | field:value
    // AND binds tighter than OR, as usual. Words are not case sensitive. A value with spaces or parentheses goes in
    // double quotes: tmdb_keyword:"time travel". Errors are FormatExceptions; the config parser adds the line number.
    public static class MatchParser
    {
        sealed class Token
        {
            public string Op;           // "(", ")", "AND", "OR", "NOT", or null for a selector
            public MatchAtom Atom;
            public int Pos;
        }

        public static MatchNode Parse(string text)
        {
            var tokens = Tokenize(text ?? "");
            if (tokens.Count == 0)
                throw new FormatException("match needs an expression, e.g. 'match tag:Christmas AND NOT genre:Horror'");
            var i = 0;
            var node = Or(tokens, ref i);
            if (i < tokens.Count)
                throw new FormatException($"unexpected '{Show(tokens[i])}' at column {tokens[i].Pos + 1}" +
                                          (tokens[i].Op == ")" ? " (no matching '(')" : " (missing AND / OR?)"));
            return node;
        }

        static string Show(Token t) => t.Op ?? t.Atom.Text;

        static MatchNode Or(List<Token> t, ref int i)
        {
            var parts = new List<MatchNode> { And(t, ref i) };
            while (i < t.Count && t[i].Op == "OR")
            {
                i++;
                parts.Add(And(t, ref i));
            }
            return parts.Count == 1 ? parts[0] : new AndOrNode(false, parts);
        }

        static MatchNode And(List<Token> t, ref int i)
        {
            var parts = new List<MatchNode> { Not(t, ref i) };
            while (i < t.Count && t[i].Op == "AND")
            {
                i++;
                parts.Add(Not(t, ref i));
            }
            return parts.Count == 1 ? parts[0] : new AndOrNode(true, parts);
        }

        static MatchNode Not(List<Token> t, ref int i)
        {
            if (i >= t.Count)
                throw new FormatException("the expression ends too early (a selector is missing after AND / OR / NOT / '(')");
            var tok = t[i++];
            if (tok.Op == "NOT")
                return new NotNode(Not(t, ref i));
            if (tok.Op == "(")
            {
                var inner = Or(t, ref i);
                if (i >= t.Count || t[i].Op != ")")
                    throw new FormatException($"'(' at column {tok.Pos + 1} is never closed");
                i++;
                return inner;
            }
            if (tok.Atom != null)
                return new AtomNode(tok.Atom);
            throw new FormatException($"unexpected '{tok.Op}' at column {tok.Pos + 1}");
        }

        static List<Token> Tokenize(string s)
        {
            var r = new List<Token>();
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                var start = i;
                if (c == '(' || c == ')')
                {
                    r.Add(new Token { Op = c.ToString(), Pos = start });
                    i++;
                    continue;
                }
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    i++;
                var word = s.Substring(start, i - start);
                if (i < s.Length && s[i] == ':' && word.Length > 0)
                {
                    i++;
                    string value;
                    if (i < s.Length && s[i] == '"')
                    {
                        var close = s.IndexOf('"', i + 1);
                        if (close < 0)
                            throw new FormatException($"the quote after '{word}:' at column {start + 1} is never closed");
                        value = s.Substring(i + 1, close - i - 1).Trim();
                        i = close + 1;
                    }
                    else
                    {
                        var vs = i;
                        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != ')')
                            i++;
                        value = s.Substring(vs, i - vs);
                    }
                    r.Add(new Token { Atom = new MatchAtom(word, value), Pos = start });
                    continue;
                }
                var up = word.ToUpperInvariant();
                if (up == "AND" || up == "OR" || up == "NOT")
                {
                    r.Add(new Token { Op = up, Pos = start });
                    continue;
                }
                throw new FormatException(word.Length == 0
                    ? $"unexpected '{c}' at column {start + 1}"
                    : $"unexpected word '{word}' at column {start + 1}; a value with spaces needs quotes, e.g. tag:\"Family Night\"");
            }
            return r;
        }
    }
}
