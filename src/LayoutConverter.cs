using System;
using System.Text;

namespace KeyboardFix
{
    /// <summary>
    /// Converts text that was typed on the wrong keyboard layout (Hebrew standard <-> English US),
    /// based on the physical key positions. Every word is flipped on its own: Hebrew words become
    /// English and English words become Hebrew, so mixed text is fully swapped.
    /// </summary>
    public static class LayoutConverter
    {
        // Same physical keys, row by row: top row, home row, bottom row.
        const string En = "qwertyuiopasdfghjkl;'zxcvbnm,./";
        const string He = "/'קראטוןםפשדגכעיחלךף,זסבהנמצתץ.";

        public enum Direction { None, ToHebrew, ToEnglish }

        static bool IsHebrewLetter(char c) { return c >= 'א' && c <= 'ת'; }
        static bool IsEnglishLetter(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }

        /// <summary>The character the same key produces on the Hebrew layout.</summary>
        public static char ToHebrew(char c)
        {
            int i = En.IndexOf(char.ToLowerInvariant(c));
            return i >= 0 ? He[i] : c;
        }

        /// <summary>The character the same key produces on the English layout.</summary>
        public static char ToEnglish(char c)
        {
            int i = He.IndexOf(c);
            return i >= 0 ? En[i] : c;
        }

        /// <param name="lastDirection">Direction of the last word that had letters (the language to switch to).</param>
        public static string Convert(string text, out Direction lastDirection)
        {
            lastDirection = Direction.None;
            var sb = new StringBuilder(text.Length);
            Direction prev = Direction.None;
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i])) { sb.Append(text[i++]); continue; }

                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                string word = text.Substring(start, i - start);

                int heb = 0, eng = 0;
                foreach (char c in word)
                {
                    if (IsHebrewLetter(c)) heb++;
                    else if (IsEnglishLetter(c)) eng++;
                }

                Direction dir;
                if (heb == 0 && eng == 0)
                    dir = prev != Direction.None ? prev : NextWordDirection(text, i); // punctuation/digits only
                else
                    dir = heb > eng ? Direction.ToEnglish : Direction.ToHebrew; // decides the word's punctuation

                foreach (char c in word)
                {
                    if (IsHebrewLetter(c)) sb.Append(ToEnglish(c));
                    else if (IsEnglishLetter(c)) sb.Append(ToHebrew(c));
                    else if (dir == Direction.ToEnglish) sb.Append(ToEnglish(c));
                    else if (dir == Direction.ToHebrew) sb.Append(ToHebrew(c));
                    else sb.Append(c);
                }

                if (heb > 0 || eng > 0) { prev = dir; lastDirection = dir; }
            }
            return sb.ToString();
        }

        static Direction NextWordDirection(string text, int from)
        {
            int heb = 0, eng = 0;
            for (int i = from; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { if (heb + eng > 0) break; continue; }
                if (IsHebrewLetter(c)) heb++;
                else if (IsEnglishLetter(c)) eng++;
            }
            if (heb == 0 && eng == 0) return Direction.None;
            return heb > eng ? Direction.ToEnglish : Direction.ToHebrew;
        }

        public static string Convert(string text)
        {
            Direction d;
            return Convert(text, out d);
        }
    }
}
