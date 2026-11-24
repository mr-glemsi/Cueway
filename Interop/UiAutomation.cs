using System.Runtime.InteropServices;
using Cueway.Media;

namespace Cueway.Interop;

internal static unsafe partial class UiAutomation
{
    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_MULTITHREADED = 0x0;
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_TabItemControlTypeId = 50019;
    private const int UIA_InvokePatternId = 10000;
    private const int UIA_SelectionItemPatternId = 10010;
    private const int TreeScope_Descendants = 4;
    private const ushort VT_I4 = 3;

    private static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private static readonly Guid IID_IUIAutomationInvokePattern = new("fb377fbe-8ea6-46d5-9c73-6499642d3059");
    private static readonly Guid IID_IUIAutomationSelectionItemPattern = new("a8efa66a-0fda-421a-9194-38021f3578ea");

    [StructLayout(LayoutKind.Sequential)]
    private struct VARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public int lVal;
        public int _pad0;
        public nint _pad1;
    }

    [LibraryImport("oleaut32.dll")]
    private static partial void SysFreeString(nint bstr);

    private static void Release(nint comPtr)
    {
        if (comPtr == 0) return;
        nint* vtbl = *(nint**)comPtr;
        ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2])(comPtr);
    }

    public static List<string> GetTabNamesForWindows(IReadOnlyList<nint> hwnds)
    {
        var names = new List<string>();
        if (hwnds.Count == 0)
            return names;

        int coHr = Win32.CoInitializeEx(0, COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;

        nint uia = 0;
        nint tabCondition = 0;

        try
        {
            Guid clsid = CLSID_CUIAutomation;
            Guid iid = IID_IUIAutomation;
            if (Win32.CoCreateInstance(&clsid, 0, CLSCTX_INPROC_SERVER, &iid, out uia) != 0 || uia == 0)
                return names;

            var variant = new VARIANT
            {
                vt = VT_I4,
                lVal = UIA_TabItemControlTypeId
            };

            nint* uiaVtbl = *(nint**)uia;
            // IUIAutomation::CreatePropertyCondition (slot 23)
            int hrCond = ((delegate* unmanaged[Stdcall]<nint, int, VARIANT, nint*, int>)uiaVtbl[23])(
                uia, UIA_ControlTypePropertyId, variant, &tabCondition);
            if (hrCond != 0 || tabCondition == 0)
                return names;

            int winCount = Math.Min(hwnds.Count, 8);
            for (int w = 0; w < winCount; w++)
            {
                nint hwnd = hwnds[w];
                if (hwnd == 0) continue;

                nint rootEl = 0;
                nint elArray = 0;
                try
                {
                    // IUIAutomation::ElementFromHandle (slot 6)
                    int hrEl = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)uiaVtbl[6])(uia, hwnd, &rootEl);
                    if (hrEl != 0 || rootEl == 0)
                        continue;

                    nint* elVtbl = *(nint**)rootEl;
                    // IUIAutomationElement::FindAll (slot 6)
                    int hrFind = ((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)elVtbl[6])(
                        rootEl, TreeScope_Descendants, tabCondition, &elArray);
                    if (hrFind != 0 || elArray == 0)
                        continue;

                    nint* arrVtbl = *(nint**)elArray;
                    int len = 0;
                    // IUIAutomationElementArray::get_Length (slot 3)
                    if (((delegate* unmanaged[Stdcall]<nint, int*, int>)arrVtbl[3])(elArray, &len) != 0 || len <= 0)
                        continue;

                    int cap = Math.Min(len, 80);
                    for (int i = 0; i < cap; i++)
                    {
                        nint itemEl = 0;
                        try
                        {
                            // IUIAutomationElementArray::GetElement (slot 4)
                            if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)arrVtbl[4])(elArray, i, &itemEl) != 0 || itemEl == 0)
                                continue;

                            nint* itemVtbl = *(nint**)itemEl;
                            nint bstr = 0;
                            // IUIAutomationElement::get_CurrentName (slot 23)
                            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)itemVtbl[23])(itemEl, &bstr) == 0 && bstr != 0)
                            {
                                try
                                {
                                    string? tabName = Marshal.PtrToStringUni(bstr);
                                    if (!string.IsNullOrWhiteSpace(tabName))
                                        names.Add(tabName);
                                }
                                finally
                                {
                                    SysFreeString(bstr);
                                }
                            }
                        }
                        finally
                        {
                            if (itemEl != 0) Release(itemEl);
                        }
                    }
                }
                finally
                {
                    if (elArray != 0) Release(elArray);
                    if (rootEl != 0) Release(rootEl);
                }
            }
        }
        catch
        {
            // Browser window might close mid-enumeration
        }
        finally
        {
            if (tabCondition != 0) Release(tabCondition);
            if (uia != 0) Release(uia);
            if (mustUninit) Win32.CoUninitialize();
        }

        return names;
    }

    public static nint TrySelectBrowserTab(
        IReadOnlyList<nint> hwnds,
        string? mediaTitle,
        string? siteDisplayName,
        string? siteKey)
    {
        if (hwnds.Count == 0)
            return 0;

        int coHr = Win32.CoInitializeEx(0, COINIT_MULTITHREADED);
        bool mustUninit = coHr == 0 || coHr == 1;

        nint uia = 0;
        nint tabCondition = 0;
        nint bestEl = 0;
        nint bestHwnd = 0;
        int bestScore = 0;

        try
        {
            Guid clsid = CLSID_CUIAutomation;
            Guid iid = IID_IUIAutomation;
            if (Win32.CoCreateInstance(&clsid, 0, CLSCTX_INPROC_SERVER, &iid, out uia) != 0 || uia == 0)
                return 0;

            var variant = new VARIANT
            {
                vt = VT_I4,
                lVal = UIA_TabItemControlTypeId
            };

            nint* uiaVtbl = *(nint**)uia;
            int hrCond = ((delegate* unmanaged[Stdcall]<nint, int, VARIANT, nint*, int>)uiaVtbl[23])(
                uia, UIA_ControlTypePropertyId, variant, &tabCondition);
            if (hrCond != 0 || tabCondition == 0)
                return 0;

            int winCount = Math.Min(hwnds.Count, 8);
            for (int w = 0; w < winCount; w++)
            {
                nint hwnd = hwnds[w];
                if (hwnd == 0) continue;

                nint rootEl = 0;
                nint elArray = 0;
                try
                {
                    if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)uiaVtbl[6])(uia, hwnd, &rootEl) != 0 || rootEl == 0)
                        continue;

                    nint* elVtbl = *(nint**)rootEl;
                    if (((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)elVtbl[6])(
                        rootEl, TreeScope_Descendants, tabCondition, &elArray) != 0 || elArray == 0)
                        continue;

                    nint* arrVtbl = *(nint**)elArray;
                    int len = 0;
                    if (((delegate* unmanaged[Stdcall]<nint, int*, int>)arrVtbl[3])(elArray, &len) != 0 || len <= 0)
                        continue;

                    int cap = Math.Min(len, 120);
                    for (int i = 0; i < cap; i++)
                    {
                        nint itemEl = 0;
                        try
                        {
                            if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)arrVtbl[4])(elArray, i, &itemEl) != 0 || itemEl == 0)
                                continue;

                            nint* itemVtbl = *(nint**)itemEl;
                            nint bstr = 0;
                            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)itemVtbl[23])(itemEl, &bstr) != 0 || bstr == 0)
                                continue;

                            string? rawName;
                            try
                            {
                                rawName = Marshal.PtrToStringUni(bstr);
                            }
                            finally
                            {
                                SysFreeString(bstr);
                            }

                            if (string.IsNullOrWhiteSpace(rawName))
                                continue;

                            int score = ScoreCandidateTab(rawName, mediaTitle, siteDisplayName, siteKey);
                            if (score > bestScore && score >= 25)
                            {
                                if (bestEl != 0) Release(bestEl);
                                bestEl = itemEl;
                                bestHwnd = hwnd;
                                bestScore = score;
                                itemEl = 0; // Ownership transferred to bestEl
                            }
                        }
                        finally
                        {
                            if (itemEl != 0) Release(itemEl);
                        }
                    }
                }
                finally
                {
                    if (elArray != 0) Release(elArray);
                    if (rootEl != 0) Release(rootEl);
                }
            }

            if (bestEl != 0 && bestHwnd != 0)
            {
                Win32.TryFocusWindow(bestHwnd);

                nint* bestVtbl = *(nint**)bestEl;
                nint selPat = 0;
                nint invPat = 0;
                bool selected = false;
                try
                {
                    Guid iidSel = IID_IUIAutomationSelectionItemPattern;
                    if (((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)bestVtbl[14])(
                        bestEl, UIA_SelectionItemPatternId, &iidSel, &selPat) == 0 && selPat != 0)
                    {
                        nint* selVtbl = *(nint**)selPat;
                        if (((delegate* unmanaged[Stdcall]<nint, int>)selVtbl[3])(selPat) == 0)
                            selected = true;
                    }

                    if (!selected)
                    {
                        Guid iidInv = IID_IUIAutomationInvokePattern;
                        if (((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)bestVtbl[14])(
                            bestEl, UIA_InvokePatternId, &iidInv, &invPat) == 0 && invPat != 0)
                        {
                            nint* invVtbl = *(nint**)invPat;
                            if (((delegate* unmanaged[Stdcall]<nint, int>)invVtbl[3])(invPat) == 0)
                                selected = true;
                        }
                    }
                }
                finally
                {
                    if (selPat != 0) Release(selPat);
                    if (invPat != 0) Release(invPat);
                }

                return selected ? bestHwnd : 0;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (bestEl != 0) Release(bestEl);
            if (tabCondition != 0) Release(tabCondition);
            if (uia != 0) Release(uia);
            if (mustUninit) Win32.CoUninitialize();
        }
    }

    private static int ScoreCandidateTab(string rawName, string? mediaTitle, string? siteDisplayName, string? siteKey)
    {
        int score = 0;
        string cleanName = BrowserSiteScanner.NormalizeBrowserWindowTitle(rawName);

        if (rawName.Contains("Ses çalıyor", StringComparison.OrdinalIgnoreCase) ||
            rawName.Contains("Ses çalınıyor", StringComparison.OrdinalIgnoreCase) ||
            rawName.Contains("Ses yürütülüyor", StringComparison.OrdinalIgnoreCase) ||
            rawName.Contains("Playing audio", StringComparison.OrdinalIgnoreCase) ||
            rawName.Contains("Audio playing", StringComparison.OrdinalIgnoreCase) ||
            rawName.Contains("Audio is playing", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
        }

        if (!string.IsNullOrWhiteSpace(siteKey))
        {
            string? detected = BrowserSiteScanner.DetectSite(rawName, allowStandaloneX: true) ??
                               BrowserSiteScanner.DetectSite(cleanName, allowStandaloneX: true);
            if (!string.IsNullOrEmpty(detected) && detected.Equals(siteKey, StringComparison.OrdinalIgnoreCase))
                score += 35;
        }

        if (!string.IsNullOrWhiteSpace(siteDisplayName) &&
            rawName.Contains(siteDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (!string.IsNullOrWhiteSpace(mediaTitle))
        {
            string t = mediaTitle.Trim();
            if (t.Length >= 3)
            {
                if (cleanName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    rawName.Contains(t, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }
                else if (cleanName.Length >= 6 && t.Contains(cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    score += 80;
                }
                else
                {
                    string[] words = t.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    int matched = 0;
                    int eligible = 0;
                    foreach (string word in words)
                    {
                        if (word.Length < 4) continue;
                        eligible++;
                        if (cleanName.Contains(word, StringComparison.OrdinalIgnoreCase))
                            matched++;
                    }
                    if (eligible > 0 && matched >= Math.Min(2, eligible))
                        score += 40 + matched * 5;
                }
            }
        }

        return score;
    }
}
