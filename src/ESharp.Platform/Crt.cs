#if SDL
namespace ESharp.Platform;

/// <summary>
/// The C runtime of the sdl target (src/ESharp.Compiler/CRuntime): es_std_* functions are standard C (stdio, stdlib, time),
/// es_sdl_* go through SDL 3. The emitter pastes the C into the generated file when a call to one of them is emitted, so a
/// program that only prints does not need SDL. The platform classes (<see cref="Dos"/>, <see cref="Video"/>, ...) wrap these.
/// </summary>
internal static unsafe class Crt
{
    [Native] public static extern int es_std_putstr(string s);
    [Native] public static extern void es_std_flush();
    [Native] public static extern byte* es_std_alloc(uint size, bool chip);
    [Native] public static extern void es_std_free(byte* block);
    [Native] public static extern uint es_std_avail(bool chip);
    [Native] public static extern int es_std_file_size(string path);
    [Native] public static extern int es_std_file_read(string path, byte* buffer, int length);
    [Native] public static extern bool es_std_file_write(string path, byte* data, int length);
    [Native] public static extern bool es_std_file_delete(string path);
    [Native] public static extern uint es_std_now();
    [Native] public static extern void es_std_trace(string s);
    [Native] public static extern void es_std_trace_char(int c);
    [Native] public static extern void es_std_trace_int(int v);
    [Native] public static extern int es_std_keychar(int raw, int qualifier);

    [Native] public static extern bool es_sdl_video_open(int width, int height, int depth);
    [Native] public static extern bool es_sdl_video_is_open();
    [Native] public static extern int es_sdl_video_depth();
    [Native] public static extern void es_sdl_video_close();
    [Native] public static extern void es_sdl_set_color(int index, int r, int g, int b);
    [Native] public static extern int es_sdl_get_color(int index);
    [Native] public static extern void es_sdl_fill(int x, int y, int width, int height, int color);
    [Native] public static extern int es_sdl_get_pixel(int x, int y);
    [Native] public static extern void es_sdl_blit(byte* planar, byte* mask, int width, int height, int depth, int sx, int sy, int w, int h, int x, int y, int planarLen, int maskLen);
    [Native] public static extern bool es_sdl_dbuf_enable();
    [Native] public static extern void es_sdl_flip(bool copy);
    [Native] public static extern void es_sdl_dbuf_disable();
    [Native] public static extern long es_sdl_ticks_us();
    [Native] public static extern void es_sdl_wait_frame(int fps);
    [Native] public static extern void es_sdl_delay(int ticks);
    [Native] public static extern void es_sdl_poll();
    [Native] public static extern void es_sdl_input_clear();
    [Native] public static extern int es_sdl_input(int field);
    [Native] public static extern void es_sdl_inject(int kind, int a, int b);
    [Native] public static extern int es_sdl_sound_open();
    [Native] public static extern void es_sdl_sound_play(int voice, byte* data, int length, int rate, int volume, int cycles);
    [Native] public static extern bool es_sdl_sound_busy(int voice);
    [Native] public static extern void es_sdl_sound_stop(int voice);
    [Native] public static extern void es_sdl_sound_close();
}
#endif
