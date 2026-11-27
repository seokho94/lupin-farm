using System.Collections.Generic;

namespace LupinFarm.Ui
{
    /// <summary>한 줄 편집기 + 이전 명령 (FR-CMD-01, 03).</summary>
    internal sealed class InputBox
    {
        string text = "";
        int caret;
        List<string> history = new List<string>();
        int histPos = -1;
        string draft = "";

        public string Text
        {
            get { return text; }
        }

        public int Caret
        {
            get { return caret; }
        }

        public void SetHistory(List<string> h)
        {
            history = h ?? new List<string>();
            histPos = -1;
        }

        public void SetText(string t)
        {
            text = t ?? "";
            caret = text.Length;
        }

        public void Insert(string s)
        {
            text = text.Substring(0, caret) + s + text.Substring(caret);
            caret += s.Length;
            histPos = -1;
        }

        public void Backspace()
        {
            if (caret == 0) return;
            int n = caret >= 2 && char.IsLowSurrogate(text[caret - 1]) ? 2 : 1;
            text = text.Substring(0, caret - n) + text.Substring(caret);
            caret -= n;
        }

        public void Delete()
        {
            if (caret >= text.Length) return;
            int n = caret + 1 < text.Length && char.IsHighSurrogate(text[caret]) ? 2 : 1;
            text = text.Substring(0, caret) + text.Substring(caret + n);
        }

        public void Left()
        {
            if (caret > 0) caret -= caret >= 2 && char.IsLowSurrogate(text[caret - 1]) ? 2 : 1;
        }

        public void Right()
        {
            if (caret < text.Length) caret += caret + 1 < text.Length && char.IsHighSurrogate(text[caret]) ? 2 : 1;
        }

        public void Home()
        {
            caret = 0;
        }

        public void End()
        {
            caret = text.Length;
        }

        public void Clear()
        {
            text = "";
            caret = 0;
            histPos = -1;
        }

        public void HistoryUp()
        {
            if (history.Count == 0) return;
            if (histPos < 0)
            {
                draft = text;
                histPos = history.Count - 1;
            }
            else if (histPos > 0)
            {
                histPos--;
            }
            SetText(history[histPos]);
        }

        public void HistoryDown()
        {
            if (histPos < 0) return;
            if (histPos < history.Count - 1)
            {
                histPos++;
                SetText(history[histPos]);
            }
            else
            {
                histPos = -1;
                SetText(draft);
            }
        }

        public void ResetHistoryCursor()
        {
            histPos = -1;
        }
    }
}
