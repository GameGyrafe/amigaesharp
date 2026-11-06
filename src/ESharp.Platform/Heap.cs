namespace ESharp.Platform;

/// <summary>
/// Heap objects (instances of non-static classes) are created with <c>new</c> and live until freed; there is no garbage
/// collector. Objects that are never freed are released when the program ends (the C runtime's allocator does that).
/// </summary>
public static class Heap
{
    /// <summary>
    /// Frees the object and sets the variable to null. Other variables that still point at the object dangle, as in C:
    /// keep one owner per object. Freeing null does nothing.
    /// </summary>
    public static void Free<T>(ref T obj) where T : class
    {
        obj = null!;
    }
}
