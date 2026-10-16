using System;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Copyless
{
    /// <summary>Reads the current text selection through UI Automation.</summary>
    static class Uia
    {
        const int MaxChars = 4000000;

        public static string GetSelectedText(Native.POINT pt, bool usePoint, bool useFocus, int timeoutMs)
        {
            string result = null;
            var t = new Thread(() =>
            {
                try
                {
                    if (usePoint)
                    {
                        AutomationElement el = null;
                        try { el = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y)); } catch { }
                        result = FromChain(el);
                    }
                    if (string.IsNullOrEmpty(result) && useFocus)
                    {
                        AutomationElement el = null;
                        try { el = AutomationElement.FocusedElement; } catch { }
                        result = FromChain(el);
                    }
                }
                catch (Exception ex) { Log.Write("uia: " + ex.Message); }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            if (!t.Join(timeoutMs)) { Log.Write("uia timed out"); return null; }
            return result;
        }

        static string FromChain(AutomationElement el)
        {
            TreeWalker walker = TreeWalker.RawViewWalker;
            for (int depth = 0; el != null && depth < 40; depth++)
            {
                try
                {
                    object isPw = el.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                    if (isPw is bool && (bool)isPw) return null;

                    object pat;
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out pat))
                    {
                        var tp = (TextPattern)pat;
                        if (tp.SupportedTextSelection != SupportedTextSelection.None)
                        {
                            var sb = new StringBuilder();
                            foreach (TextPatternRange r in tp.GetSelection())
                            {
                                string s = r.GetText(MaxChars);
                                if (string.IsNullOrEmpty(s)) continue;
                                if (sb.Length > 0) sb.Append("\r\n");
                                sb.Append(s);
                            }
                            // Stop at the nearest text container.
                            return sb.ToString();
                        }
                    }
                }
                catch { }
                try { el = walker.GetParent(el); } catch { return null; }
            }
            return null;
        }
    }
}
