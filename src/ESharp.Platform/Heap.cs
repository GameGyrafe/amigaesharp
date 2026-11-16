namespace ESharp.Platform;

/// <summary>
/// Heap objects (instances of non-static classes) are created with <c>new</c> and live until freed; there is no garbage
/// collector. Objects that are never freed are released when the program ends (the C runtime's allocator does that).
/// Stored closures (capturing lambdas kept in a field, an event, a static or an array, or returned) are freed the same way;
/// see docs/STATUS.md "Heap closures and events".
/// </summary>
public static class Heap
{
    /// <summary>
    /// Frees the object and sets the variable to null. Other variables that still point at the object dangle, as in C:
    /// keep one owner per object. Freeing null does nothing.
    /// On a delegate it drops that delegate's reference to its heap closure (the captured variables are freed when the
    /// method that made them has returned and no other closure uses them); on an event (inside its class) it drops every
    /// handler. A delegate without captures has nothing to free.
    /// </summary>
    public static void Free<T>(ref T obj) where T : class
    {
        obj = null!;
    }

    /// <summary>
    /// The number of heap closure references not yet freed: one per stored capturing lambda that was made and not freed
    /// with Heap.Free, plus one per handler an event holds. Compare it before and after to find a closure that was never
    /// freed. On the dotnet host the garbage collector owns closures, so this is always 0 there.
    /// </summary>
    public static int LiveClosures() => 0;
}
