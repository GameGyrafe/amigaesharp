/* Measures how far the "PUBSCREEN handler" task of AROS's muiscreen.library writes below its stack (issue #105).
 *
 * muiscreen.library's init starts that task with AllocMem(sizeof(struct Task), MEMF_PUBLIC | MEMF_CLEAR) and a stack of
 * AllocMem(4096, MEMF_PUBLIC), on the task that opens the library first (muimaster.library opens it in its own init). This
 * program opens muiscreen.library with exec's AllocMem patched for its own task, so that the stack request gets PAD bytes of a
 * known pattern below it, waits a second and prints how many of those bytes the handler task changed.
 *
 * Build:  i386-aros-gcc --sysroot=/opt/i386-aros/sdk -O2 -o muiscreen-stack muiscreen-stack.c          (esharp-aros-i386)
 *         x86_64-aros-gcc --sysroot=/opt/x86_64-aros -O2 -o muiscreen-stack muiscreen-stack.c          (esharp-amiga-toolchain)
 * Run it first thing after boot, before anything has opened muimaster.library (from S:Startup-Sequence, as
 * tools/aros-hosted/aros-hosted.sh does with a sample). It leaks PAD + 4096 bytes on purpose: the library never frees the stack.
 */
#include <proto/exec.h>
#include <proto/dos.h>
#include <exec/memory.h>
#include <exec/tasks.h>
#include <exec/execbase.h>
#include <aros/libcall.h>

#define PAD 16384

static struct Task *me;
static int step;
static UBYTE *block, *stack;
static APTR old;

static AROS_LH2(APTR, padded_allocmem, AROS_LHA(IPTR, byteSize, D0), AROS_LHA(ULONG, requirements, D1), struct ExecBase *, SysBase, 33, Diag)
{
    AROS_LIBFUNC_INIT
    if (step < 2 && FindTask(NULL) == me)
    {
        if (step == 1 && byteSize == 4096 && requirements == MEMF_PUBLIC)
        {
            step = 2;
            block = (UBYTE *)AROS_CALL2(APTR, old, AROS_LCA(IPTR, byteSize + PAD, D0), AROS_LCA(ULONG, requirements, D1), struct ExecBase *, SysBase);
            if (block != NULL)
            {
                for (int i = 0; i < PAD + 4096; i++) block[i] = 0xA5;
                stack = block + PAD;
                return stack;
            }
        }
        else step = byteSize >= sizeof(struct Task) && byteSize <= sizeof(struct Task) + 1024 && requirements == (MEMF_PUBLIC | MEMF_CLEAR) ? 1 : 0;   /* the system's struct Task may be larger than ours */
    }
    return AROS_CALL2(APTR, old, AROS_LCA(IPTR, byteSize, D0), AROS_LCA(ULONG, requirements, D1), struct ExecBase *, SysBase);
    AROS_LIBFUNC_EXIT
}

static void say(const char *format, IPTR value)
{
    IPTR args[1];
    args[0] = value;
    VPrintf((CONST_STRPTR)format, (RAWARG)args);
}

int main(void)
{
    APTR mine = (APTR)AROS_SLIB_ENTRY(padded_allocmem, Diag, 33);
    struct Library *ms;
    struct Task *handler;
    int loaded, lowest, untouched;

    me = FindTask(NULL);
    Forbid();
    loaded = FindName(&SysBase->LibList, (CONST_STRPTR)"muiscreen.library") != NULL;
    Permit();
    if (loaded) { PutStr((CONST_STRPTR)"muiscreen.library is loaded already: run this first after boot\n"); return 5; }
    Forbid();
    old = SetFunction((struct Library *)SysBase, -33 * (LONG)LIB_VECTSIZE, mine);
    Permit();
    ms = OpenLibrary((CONST_STRPTR)"muiscreen.library", 0);
    step = 2;
    Forbid();
    SetFunction((struct Library *)SysBase, -33 * (LONG)LIB_VECTSIZE, old);
    Permit();
    Delay(50);
    Forbid();
    handler = FindTask((CONST_STRPTR)"PUBSCREEN handler");
    Permit();
    say("muiscreen.library %lx\n", (IPTR)ms);
    say("handler task %lx\n", (IPTR)handler);
    say("its stack starts at %lx\n", handler != NULL ? (IPTR)handler->tc_SPLower : 0);
    say("the padded stack is at %lx\n", (IPTR)stack);
    if (stack != NULL && handler != NULL && (UBYTE *)handler->tc_SPLower == stack)
    {
        lowest = 0;
        while (lowest < PAD && block[lowest] == 0xA5) lowest++;
        untouched = 0;
        while (untouched < 4096 && stack[untouched] == 0xA5) untouched++;
        say("stack bytes never written, from its bottom: %ld\n", untouched);
        say("deepest write below the stack: %ld bytes\n", PAD - lowest);
    }
    if (ms != NULL) CloseLibrary(ms);
    return 0;
}
