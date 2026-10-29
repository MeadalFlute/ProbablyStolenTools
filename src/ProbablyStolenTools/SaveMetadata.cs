using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProbablyStolenTools
{
    internal sealed record SaveMetadata(int Day, int Cash, int State, int Slot, string RunId = null)
    {
        internal static SaveMetadata Read(string text)
        {
            var reader = new Reader(text);
            reader.Object("");
            reader.White();
            if (reader.Position != text.Length) throw new FormatException("Unexpected ES3 suffix.");
            int Get(string key) => reader.Values.TryGetValue(key, out int value)
                ? value : throw new FormatException("Missing ES3 metadata: " + key);
            return new SaveMetadata(Get("storeStation.value.dayCounter"), Get("playerStore.value.playerCash"),
                Get("playerStore.value.storeState"), Get("playerStore.value.saveSlotId"), reader.RunId);
        }

        // ES3 is not strict JSON in this build: numeric dictionary keys can be unquoted,
        // and strings can contain escaped typographic quotes. Read only the exact metadata
        // paths, structurally skipping other values without rewriting the user's file.
        private sealed class Reader
        {
            private readonly string _text;
            internal int Position;
            internal string RunId;
            internal readonly Dictionary<string, int> Values = new();
            internal Reader(string text)
            {
                if (text == null || text.Length > 16 * 1024 * 1024) throw new FormatException("Invalid ES3 size.");
                _text = text;
            }
            internal void White() { while (Position < _text.Length && char.IsWhiteSpace(_text[Position])) Position++; }
            private char Peek() { White(); return Position < _text.Length ? _text[Position] : '\0'; }
            private void Expect(char c)
            {
                if (Peek() != c) throw new FormatException("Malformed ES3 metadata.");
                Position++;
            }
            private string Key()
            {
                Expect('"');
                int start = Position;
                while (Position < _text.Length && _text[Position] != '"')
                {
                    if (_text[Position] == '\\') throw new FormatException("Escaped metadata key.");
                    Position++;
                }
                string value = _text.Substring(start, Position - start);
                Expect('"');
                return value;
            }
            internal void Object(string path)
            {
                Expect('{');
                if (Peek() == '}') { Position++; return; }
                while (true)
                {
                    string key = Key();
                    Expect(':');
                    string next = path.Length == 0 ? key : path + "." + key;
                    if (next == "playerStore" || next == "storeStation"
                        || next == "playerStore.value" || next == "storeStation.value")
                        Object(next);
                    else if (next == "playerStore.value.runID")
                    {
                        if (RunId != null) throw new FormatException("Duplicate ES3 run ID.");
                        RunId = Key();
                    }
                    else if (next == "playerStore.value.playerCash" || next == "playerStore.value.storeState"
                        || next == "playerStore.value.saveSlotId" || next == "storeStation.value.dayCounter")
                    {
                        int start = Position;
                        SkipValue(0);
                        int value = int.Parse(_text.Substring(start, Position - start).Trim(),
                            NumberStyles.Integer, CultureInfo.InvariantCulture);
                        if (!Values.TryAdd(next, value)) throw new FormatException("Duplicate ES3 metadata.");
                    }
                    else SkipValue(0);
                    if (Peek() == '}') { Position++; return; }
                    Expect(',');
                }
            }
            private void SkipValue(int depth)
            {
                if (depth > 128) throw new FormatException("ES3 nesting limit.");
                char c = Peek();
                if (c == '"')
                {
                    Position++;
                    while (Position < _text.Length)
                    {
                        c = _text[Position++];
                        if (c == '"') return;
                        if (c == '\\') Position++;
                    }
                    throw new FormatException("Unterminated ES3 string.");
                }
                if (c == '{' || c == '[')
                {
                    Position++;
                    char end = c == '{' ? '}' : ']';
                    while (Peek() != end)
                    {
                        if (Peek() == '\0') throw new FormatException("Unterminated ES3 container.");
                        if (Peek() == ',' || Peek() == ':') Position++;
                        else SkipValue(depth + 1);
                    }
                    Position++;
                    return;
                }
                int start = Position;
                while (Position < _text.Length && ",:}] \t\r\n".IndexOf(_text[Position]) < 0) Position++;
                if (Position == start) throw new FormatException("Missing ES3 value.");
            }
        }
    }
}
