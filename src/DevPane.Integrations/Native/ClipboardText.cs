using System.Runtime.InteropServices;

namespace DevPane.Integrations.Native;

/// <summary>
/// Puts text on the Windows clipboard with the Win32 clipboard functions, which work from a process without a window,
/// like a widget provider.
/// </summary>
public static unsafe partial class ClipboardText
{
    private const uint UnicodeTextFormat = 13; // CF_UNICODETEXT
    private const uint MoveableMemory = 0x0002; // GMEM_MOVEABLE

    // Another app can hold the clipboard open for a moment, so opening it is retried briefly.
    private const int OpenAttempts = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>Replaces the clipboard's contents with <paramref name="text"/>. Returns false when the clipboard stayed busy or the copy failed.</summary>
    public static bool TrySet(string text)
    {
        for (int attempt = 1; !OpenClipboard(0); attempt++)
        {
            if (attempt == OpenAttempts)
            {
                return false;
            }

            Thread.Sleep(OpenRetryDelay);
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            // The clipboard takes ownership of the memory once SetClipboardData succeeds.
            nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
            nint memory = GlobalAlloc(MoveableMemory, bytes);
            if (memory == 0)
            {
                return false;
            }

            char* target = (char*)GlobalLock(memory);
            if (target == null)
            {
                GlobalFree(memory);
                return false;
            }

            text.AsSpan().CopyTo(new Span<char>(target, text.Length));
            target[text.Length] = '\0';
            GlobalUnlock(memory);

            if (SetClipboardData(UnicodeTextFormat, memory) == 0)
            {
                GlobalFree(memory);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint newOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial void* GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);
}
