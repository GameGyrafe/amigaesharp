/* ---- E# runtime for the sdl target: video, input, timing and sound through SDL 3 (zlib licence). ----
   The screen is a chunky 8-bit framebuffer with a 256-entry palette, like the Amiga screen the same game opens; every
   present converts it to ARGB8888 into a streaming texture that is drawn integer-scaled into a resizable window.
   Environment (set by esharp run --target sdl): ESHARP_INPUT is a scripted input string replayed after the program
   prints SHOT, ESHARP_SCREENSHOT a file the frame is written to (binary PPM) once the script has played,
   ESHARP_SCALE the window scale (default: as large as fits on the desktop; 1 with SDL_VIDEODRIVER=offscreen). */
#include <SDL3/SDL.h>

static SDL_Window *es_sdl_win;
static SDL_Renderer *es_sdl_ren;
static SDL_Texture *es_sdl_tex;
static uint8_t *es_sdl_fb;
static uint32_t *es_sdl_argb;
static uint32_t es_sdl_pal[256];
static int32_t es_sdl_w, es_sdl_h, es_sdl_depth;
static bool es_sdl_quit_hooked;

static void es_sdl_atexit(void);

static bool es_sdl_init(SDL_InitFlags flags)
{
    if (!es_sdl_quit_hooked) { es_sdl_quit_hooked = true; atexit(es_sdl_atexit); }
    if (SDL_WasInit(flags) == flags) return true;
    if (!SDL_InitSubSystem(flags)) { fprintf(stderr, "SDL: %s\n", SDL_GetError()); return false; }
    return true;
}

/* ---- scripted input and the screenshot ---- */

static bool es_sdl_shot_seen;      /* the program printed SHOT */
static const char *es_sdl_script;  /* the rest of ESHARP_INPUT, or NULL */
static bool es_sdl_script_read;
static int32_t es_sdl_script_wait; /* polls to wait before the next step */
static bool es_sdl_script_release; /* a scripted click releases the button on the next poll */
static int32_t es_sdl_settle = -1; /* presents since the script finished; the screenshot is taken at 5 */
static bool es_sdl_shot_done;

static void es_sdl_out_hook(const char *s)
{
    if (!es_sdl_shot_seen && strstr(s, "SHOT") != NULL) es_sdl_shot_seen = true;
}

static void es_sdl_screenshot(void)
{
    const char *path = getenv("ESHARP_SCREENSHOT");
    if (es_sdl_shot_done || path == NULL || path[0] == 0 || es_sdl_fb == NULL) return;
    es_sdl_shot_done = true;
    FILE *f = fopen(path, "wb");
    if (f == NULL) { fprintf(stderr, "esharp: cannot write %s\n", path); return; }
    fprintf(f, "P6\n%ld %ld\n255\n", (long)es_sdl_w, (long)es_sdl_h);
    for (int32_t i = 0; i < es_sdl_w * es_sdl_h; i++)
    {
        uint32_t c = es_sdl_pal[es_sdl_fb[i]];
        fputc((int)((c >> 16) & 255), f);
        fputc((int)((c >> 8) & 255), f);
        fputc((int)(c & 255), f);
    }
    fclose(f);
}

static void es_sdl_atexit(void)
{
    es_sdl_screenshot();
    SDL_Quit();
}

/* ---- video ---- */

static __attribute__((unused)) bool es_sdl_video_open(int32_t w, int32_t h, int32_t depth)
{
    if (es_sdl_fb != NULL || w <= 0 || h <= 0 || depth < 1 || depth > 8) return false;
    es_std_out_hook = es_sdl_out_hook;
    if (!es_sdl_init(SDL_INIT_VIDEO)) return false;
    int scale = 1;
    const char *env = getenv("ESHARP_SCALE");
    const char *driver = SDL_GetCurrentVideoDriver();
    if (env != NULL && atoi(env) > 0) scale = atoi(env);
    else if (driver == NULL || strcmp(driver, "offscreen") != 0)
    {
        SDL_Rect r;
        if (SDL_GetDisplayUsableBounds(SDL_GetPrimaryDisplay(), &r))
        {
            int sx = r.w * 9 / 10 / w, sy = r.h * 9 / 10 / h;
            scale = sx < sy ? sx : sy;
            if (scale < 1) scale = 1;
        }
    }
    if (!SDL_CreateWindowAndRenderer("E#", w * scale, h * scale, SDL_WINDOW_RESIZABLE, &es_sdl_win, &es_sdl_ren))
    {
        fprintf(stderr, "SDL: %s\n", SDL_GetError());
        return false;
    }
    SDL_SetRenderLogicalPresentation(es_sdl_ren, w, h, SDL_LOGICAL_PRESENTATION_INTEGER_SCALE);
    es_sdl_tex = SDL_CreateTexture(es_sdl_ren, SDL_PIXELFORMAT_ARGB8888, SDL_TEXTUREACCESS_STREAMING, w, h);
    es_sdl_fb = (uint8_t *)calloc((size_t)w * (size_t)h, 1);
    es_sdl_argb = (uint32_t *)calloc((size_t)w * (size_t)h, 4);
    if (es_sdl_tex == NULL || es_sdl_fb == NULL || es_sdl_argb == NULL)
    {
        fprintf(stderr, "SDL: %s\n", SDL_GetError());
        if (es_sdl_tex != NULL) SDL_DestroyTexture(es_sdl_tex);
        SDL_DestroyRenderer(es_sdl_ren);
        SDL_DestroyWindow(es_sdl_win);
        free(es_sdl_fb);
        free(es_sdl_argb);
        es_sdl_tex = NULL; es_sdl_ren = NULL; es_sdl_win = NULL; es_sdl_fb = NULL; es_sdl_argb = NULL;
        return false;
    }
    SDL_SetTextureScaleMode(es_sdl_tex, SDL_SCALEMODE_NEAREST);
    for (int i = 0; i < 256; i++) es_sdl_pal[i] = 0xFF000000u;
    es_sdl_w = w;
    es_sdl_h = h;
    es_sdl_depth = depth;
    return true;
}

static __attribute__((unused)) bool es_sdl_video_is_open(void) { return es_sdl_fb != NULL; }

static __attribute__((unused)) int32_t es_sdl_video_depth(void) { return es_sdl_depth; }

static void es_sdl_present(void)
{
    if (es_sdl_fb == NULL) return;
    int32_t n = es_sdl_w * es_sdl_h;
    for (int32_t i = 0; i < n; i++) es_sdl_argb[i] = es_sdl_pal[es_sdl_fb[i]];
    SDL_UpdateTexture(es_sdl_tex, NULL, es_sdl_argb, es_sdl_w * 4);
    SDL_SetRenderDrawColor(es_sdl_ren, 0, 0, 0, 255);
    SDL_RenderClear(es_sdl_ren);
    SDL_RenderTexture(es_sdl_ren, es_sdl_tex, NULL, NULL);
    SDL_RenderPresent(es_sdl_ren);
    if (es_sdl_settle >= 0 && ++es_sdl_settle >= 5) es_sdl_screenshot();
}

static __attribute__((unused)) void es_sdl_video_close(void)
{
    if (es_sdl_fb == NULL) return;
    es_sdl_screenshot();
    SDL_DestroyTexture(es_sdl_tex);
    SDL_DestroyRenderer(es_sdl_ren);
    SDL_DestroyWindow(es_sdl_win);
    free(es_sdl_fb);
    free(es_sdl_argb);
    es_sdl_tex = NULL; es_sdl_ren = NULL; es_sdl_win = NULL; es_sdl_fb = NULL; es_sdl_argb = NULL;
    es_sdl_w = es_sdl_h = es_sdl_depth = 0;
}

static __attribute__((unused)) void es_sdl_set_color(int32_t index, int32_t r, int32_t g, int32_t b)
{
    if (index < 0 || index > 255) return;
    es_sdl_pal[index] = 0xFF000000u | ((uint32_t)(r & 255) << 16) | ((uint32_t)(g & 255) << 8) | (uint32_t)(b & 255);
}

static __attribute__((unused)) int32_t es_sdl_get_color(int32_t index)
{
    return index < 0 || index > 255 ? 0 : (int32_t)(es_sdl_pal[index] & 0xFFFFFFu);
}

static __attribute__((unused)) void es_sdl_fill(int32_t x, int32_t y, int32_t w, int32_t h, int32_t color)
{
    if (es_sdl_fb == NULL || w <= 0 || h <= 0) return;
    int32_t x0 = x < 0 ? 0 : x, y0 = y < 0 ? 0 : y;
    int32_t x1 = x + w > es_sdl_w ? es_sdl_w : x + w, y1 = y + h > es_sdl_h ? es_sdl_h : y + h;
    if (x0 >= x1 || y0 >= y1) return;
    uint8_t c = (uint8_t)(color & ((1 << es_sdl_depth) - 1));
    for (int32_t row = y0; row < y1; row++) memset(es_sdl_fb + (size_t)row * es_sdl_w + x0, c, (size_t)(x1 - x0));
}

static __attribute__((unused)) int32_t es_sdl_get_pixel(int32_t x, int32_t y)
{
    if (es_sdl_fb == NULL || x < 0 || y < 0 || x >= es_sdl_w || y >= es_sdl_h) return -1;
    return es_sdl_fb[y * es_sdl_w + x];
}

/* A planar image (plane after plane, rows padded to 16 pixels) or a region of it, optionally through a one-plane mask.
   Planes the image does not have are zero, as with the Amiga blit (every plane of the screen is written). */
static __attribute__((unused)) void es_sdl_blit(const uint8_t *planar, const uint8_t *mask, int32_t width, int32_t height, int32_t depth,
    int32_t sx, int32_t sy, int32_t w, int32_t h, int32_t x, int32_t y)
{
    if (es_sdl_fb == NULL || planar == NULL || w <= 0 || h <= 0) return;
    int32_t bpr = (width + 15) / 16 * 2;
    int32_t plane = bpr * height;
    for (int32_t row = 0; row < h; row++)
    {
        int32_t srcY = sy + row, py = y + row;
        if (srcY < 0 || srcY >= height || py < 0 || py >= es_sdl_h) continue;
        for (int32_t col = 0; col < w; col++)
        {
            int32_t srcX = sx + col, px = x + col;
            if (srcX < 0 || srcX >= width || px < 0 || px >= es_sdl_w) continue;
            int32_t o = srcY * bpr + (srcX >> 3), bit = 0x80 >> (srcX & 7);
            if (mask != NULL && (mask[o] & bit) == 0) continue;
            int32_t v = 0;
            for (int32_t p = 0; p < depth; p++) if (planar[p * plane + o] & bit) v |= 1 << p;
            es_sdl_fb[py * es_sdl_w + px] = (uint8_t)(v & ((1 << es_sdl_depth) - 1));
        }
    }
}

/* ---- timing ---- */

static uint64_t es_sdl_next_frame;

static __attribute__((unused)) int64_t es_sdl_ticks_us(void) { return (int64_t)(SDL_GetTicksNS() / 1000u); }

static void es_sdl_pump(void) { if (es_sdl_win != NULL) SDL_PumpEvents(); }

/* Shows the frame and waits for the next 1/fps second (a fixed pace: the display's own rate may be anything). */
static __attribute__((unused)) void es_sdl_wait_frame(int32_t fps)
{
    es_sdl_present();
    es_sdl_pump();
    uint64_t step = 1000000000u / (uint64_t)(fps > 0 ? fps : 50);
    uint64_t now = SDL_GetTicksNS();
    if (es_sdl_next_frame == 0 || now > es_sdl_next_frame + 4 * step) es_sdl_next_frame = now;   /* first frame, or far behind: restart */
    es_sdl_next_frame += step;
    if (es_sdl_next_frame > now) SDL_DelayPrecise(es_sdl_next_frame - now);
}

/* dos.library Delay: ticks of 1/50 second. The screen is shown first, so a program that waits sees its drawing. */
static __attribute__((unused)) void es_sdl_delay(int32_t ticks)
{
    es_sdl_present();
    es_sdl_pump();
    if (ticks > 0) SDL_Delay((uint32_t)ticks * 20u);
    es_sdl_next_frame = 0;
}

/* ---- input: mouse and keys, as Amiga raw key codes and qualifier bits ---- */

static int32_t es_sdl_in[9];   /* mouse x, y, left down, pressed, released, press x, press y, last key, qualifier */
static int32_t es_sdl_quits;

static int32_t es_sdl_rawkey(SDL_Scancode s)
{
    static const signed char letters[26] =
    {   /* A..Z */
        0x20, 0x35, 0x33, 0x22, 0x12, 0x23, 0x24, 0x25, 0x17, 0x26, 0x27, 0x28, 0x37,
        0x36, 0x18, 0x19, 0x10, 0x13, 0x21, 0x14, 0x16, 0x34, 0x11, 0x32, 0x15, 0x31,
    };
    if (s >= SDL_SCANCODE_A && s <= SDL_SCANCODE_Z) return letters[s - SDL_SCANCODE_A];
    if (s >= SDL_SCANCODE_1 && s <= SDL_SCANCODE_0) return 0x01 + (s - SDL_SCANCODE_1);
    if (s >= SDL_SCANCODE_F1 && s <= SDL_SCANCODE_F10) return 0x50 + (s - SDL_SCANCODE_F1);
    switch (s)
    {
        case SDL_SCANCODE_RETURN: return 0x44;
        case SDL_SCANCODE_ESCAPE: return 0x45;
        case SDL_SCANCODE_BACKSPACE: return 0x41;
        case SDL_SCANCODE_TAB: return 0x42;
        case SDL_SCANCODE_SPACE: return 0x40;
        case SDL_SCANCODE_MINUS: return 0x0B;
        case SDL_SCANCODE_EQUALS: return 0x0C;
        case SDL_SCANCODE_LEFTBRACKET: return 0x1A;
        case SDL_SCANCODE_RIGHTBRACKET: return 0x1B;
        case SDL_SCANCODE_BACKSLASH: return 0x0D;
        case SDL_SCANCODE_SEMICOLON: return 0x29;
        case SDL_SCANCODE_APOSTROPHE: return 0x2A;
        case SDL_SCANCODE_GRAVE: return 0x00;
        case SDL_SCANCODE_COMMA: return 0x38;
        case SDL_SCANCODE_PERIOD: return 0x39;
        case SDL_SCANCODE_SLASH: return 0x3A;
        case SDL_SCANCODE_DELETE: return 0x46;
        case SDL_SCANCODE_UP: return 0x4C;
        case SDL_SCANCODE_DOWN: return 0x4D;
        case SDL_SCANCODE_RIGHT: return 0x4E;
        case SDL_SCANCODE_LEFT: return 0x4F;
        case SDL_SCANCODE_KP_ENTER: return 0x43;
        case SDL_SCANCODE_LSHIFT: return 0x60;
        case SDL_SCANCODE_RSHIFT: return 0x61;
        case SDL_SCANCODE_CAPSLOCK: return 0x62;
        case SDL_SCANCODE_LCTRL: case SDL_SCANCODE_RCTRL: return 0x63;
        case SDL_SCANCODE_LALT: return 0x64;
        case SDL_SCANCODE_RALT: return 0x65;
        case SDL_SCANCODE_LGUI: return 0x66;
        case SDL_SCANCODE_RGUI: return 0x67;
        default: return -1;
    }
}

static int32_t es_sdl_qualifier(SDL_Keymod m)
{
    int32_t q = 0;
    if (m & SDL_KMOD_LSHIFT) q |= 1;
    if (m & SDL_KMOD_RSHIFT) q |= 2;
    if (m & SDL_KMOD_CAPS) q |= 4;
    if (m & SDL_KMOD_CTRL) q |= 8;
    if (m & SDL_KMOD_LALT) q |= 16;
    if (m & SDL_KMOD_RALT) q |= 32;
    if (m & SDL_KMOD_LGUI) q |= 64;
    if (m & SDL_KMOD_RGUI) q |= 128;
    return q;
}

static int32_t es_sdl_clamp(int32_t v, int32_t hi) { return v < 0 ? 0 : (v >= hi ? (hi > 0 ? hi - 1 : 0) : v); }

/* One step of ESHARP_INPUT per poll (with gaps), once SHOT has been printed. Steps: "click X,Y" (screen pixels),
   "rel DX,DY", "press", "key A+B" (Amiga raw codes; all but the last are held as qualifiers). */
static void es_sdl_script_step(void)
{
    if (!es_sdl_script_read)
    {
        es_sdl_script_read = true;
        const char *s = getenv("ESHARP_INPUT");
        es_sdl_script = s != NULL && s[0] != 0 ? s : NULL;
    }
    if (!es_sdl_shot_seen) return;
    if (es_sdl_script_release)
    {
        es_sdl_script_release = false;
        es_sdl_in[2] = 0;
        es_sdl_in[4] = 1;
        es_sdl_script_wait = 10;
        return;
    }
    if (es_sdl_script_wait > 0) { es_sdl_script_wait--; return; }
    if (es_sdl_script == NULL)
    {
        if (es_sdl_settle < 0) es_sdl_settle = 0;
        return;
    }
    while (*es_sdl_script == ' ' || *es_sdl_script == ';') es_sdl_script++;
    if (*es_sdl_script == 0) { es_sdl_script = NULL; return; }
    char step[128];
    size_t n = 0;
    while (es_sdl_script[n] != 0 && es_sdl_script[n] != ';' && n < sizeof step - 1) { step[n] = es_sdl_script[n]; n++; }
    step[n] = 0;
    es_sdl_script += n;
    long a = 0, b = 0;
    if (sscanf(step, " click %ld , %ld", &a, &b) == 2 || strcmp(step, "press") == 0 || strncmp(step, "press ", 6) == 0)
    {
        if (step[0] != 'p') { es_sdl_in[0] = es_sdl_clamp((int32_t)a, es_sdl_w); es_sdl_in[1] = es_sdl_clamp((int32_t)b, es_sdl_h); }
        es_sdl_in[2] = 1;
        es_sdl_in[3] = 1;
        es_sdl_in[5] = es_sdl_in[0];
        es_sdl_in[6] = es_sdl_in[1];
        es_sdl_script_release = true;
    }
    else if (sscanf(step, " rel %ld , %ld", &a, &b) == 2)
    {
        es_sdl_in[0] = es_sdl_clamp(es_sdl_in[0] + (int32_t)a, es_sdl_w);
        es_sdl_in[1] = es_sdl_clamp(es_sdl_in[1] + (int32_t)b, es_sdl_h);
        es_sdl_script_wait = 10;
    }
    else if (strncmp(step, "key ", 4) == 0)
    {
        const char *p = step + 4;
        int32_t q = 0, last = -1;
        while (*p != 0)
        {
            char *end;
            long code = strtol(p, &end, 10);
            if (end == p) break;
            if (last >= 0x60 && last <= 0x67) q |= 1 << (last - 0x60);
            last = (int32_t)code;
            p = *end == '+' ? end + 1 : end;
        }
        if (last >= 0) { es_sdl_in[7] = last; es_sdl_in[8] = q; }
        es_sdl_script_wait = 10;
    }
    else
    {
        fprintf(stderr, "esharp: bad input step '%s'\n", step);
    }
}

static void es_sdl_apply_injected(void);

/* Reads pending events into es_sdl_in (the "since last poll" fields were cleared by the caller's es_sdl_input_clear). */
static __attribute__((unused)) void es_sdl_poll(void)
{
    SDL_Event ev;
    while (es_sdl_win != NULL && SDL_PollEvent(&ev))
    {
        switch (ev.type)
        {
            case SDL_EVENT_QUIT:
            case SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                /* Closing the window is Esc (the games quit on it); a second close ends the program. */
                if (++es_sdl_quits > 1) exit(0);
                es_sdl_in[7] = 0x45;
                es_sdl_in[8] = 0;
                break;
            case SDL_EVENT_MOUSE_MOTION:
                SDL_ConvertEventToRenderCoordinates(es_sdl_ren, &ev);
                es_sdl_in[0] = es_sdl_clamp((int32_t)ev.motion.x, es_sdl_w);
                es_sdl_in[1] = es_sdl_clamp((int32_t)ev.motion.y, es_sdl_h);
                break;
            case SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EVENT_MOUSE_BUTTON_UP:
                SDL_ConvertEventToRenderCoordinates(es_sdl_ren, &ev);
                es_sdl_in[0] = es_sdl_clamp((int32_t)ev.button.x, es_sdl_w);
                es_sdl_in[1] = es_sdl_clamp((int32_t)ev.button.y, es_sdl_h);
                if (ev.button.button != SDL_BUTTON_LEFT) break;
                if (ev.type == SDL_EVENT_MOUSE_BUTTON_DOWN)
                {
                    es_sdl_in[2] = 1; es_sdl_in[3] = 1; es_sdl_in[5] = es_sdl_in[0]; es_sdl_in[6] = es_sdl_in[1];
                }
                else
                {
                    es_sdl_in[2] = 0; es_sdl_in[4] = 1;
                }
                break;
            case SDL_EVENT_KEY_DOWN:
            {
                int32_t raw = es_sdl_rawkey(ev.key.scancode);
                if (raw < 0) break;
                es_sdl_in[7] = raw;
                es_sdl_in[8] = es_sdl_qualifier(ev.key.mod) | (ev.key.repeat ? 0x200 : 0);
                break;
            }
            default:
                break;
        }
    }
    es_sdl_apply_injected();
    es_sdl_script_step();
}

/* Input.HostMove/HostPress/HostRelease/HostKey on SDL: events made by the program itself (its self-checks), as on the dotnet host.
   kind 0 moves the pointer now; 1 press, 2 release and 3 key (a = raw code, b = qualifier) arrive with the next poll. */
static bool es_sdl_inj_press, es_sdl_inj_release;
static int32_t es_sdl_inj_key, es_sdl_inj_qual;

static __attribute__((unused)) void es_sdl_inject(int32_t kind, int32_t a, int32_t b)
{
    switch (kind)
    {
        case 0: es_sdl_in[0] = a; es_sdl_in[1] = b; break;
        case 1: es_sdl_inj_press = true; break;
        case 2: es_sdl_inj_release = true; break;
        case 3: es_sdl_inj_key = a; es_sdl_inj_qual = b; break;
        default: break;
    }
}

static void es_sdl_apply_injected(void)
{
    if (es_sdl_inj_press) { es_sdl_in[2] = 1; es_sdl_in[3] = 1; es_sdl_in[5] = es_sdl_in[0]; es_sdl_in[6] = es_sdl_in[1]; es_sdl_inj_press = false; }
    if (es_sdl_inj_release) { es_sdl_in[2] = 0; es_sdl_in[4] = 1; es_sdl_inj_release = false; }
    if (es_sdl_inj_key != 0) { es_sdl_in[7] = es_sdl_inj_key; es_sdl_in[8] = es_sdl_inj_qual; es_sdl_inj_key = 0; }
}

static __attribute__((unused)) void es_sdl_input_clear(void) { es_sdl_in[3] = 0; es_sdl_in[4] = 0; es_sdl_in[7] = 0; es_sdl_in[8] = 0; }

static __attribute__((unused)) int32_t es_sdl_input(int32_t field) { return field >= 0 && field < 9 ? es_sdl_in[field] : 0; }

/* ---- sound: four voices, each an SDL audio stream of signed 8-bit mono at the sample's rate, fed from a callback ---- */

typedef struct { const int8_t *data; int32_t len, pos, cycles; bool active; } EsSdlVoice;
static EsSdlVoice es_sdl_voice[4];
static SDL_AudioStream *es_sdl_vs[4];
static SDL_AudioDeviceID es_sdl_adev;
static int32_t es_sdl_voices;

static void SDLCALL es_sdl_feed(void *userdata, SDL_AudioStream *stream, int additional, int total)
{
    (void)total;
    EsSdlVoice *v = (EsSdlVoice *)userdata;
    while (additional > 0 && v->active)
    {
        int32_t n = v->len - v->pos;
        if (n > additional) n = additional;
        SDL_PutAudioStreamData(stream, v->data + v->pos, n);
        v->pos += n;
        additional -= n;
        if (v->pos >= v->len)
        {
            v->pos = 0;
            if (v->cycles > 0 && --v->cycles == 0)
            {
                v->active = false;
                SDL_FlushAudioStream(stream);   /* the resampler keeps a tail until flushed; the voice is busy until it has played */
            }
        }
    }
}

static __attribute__((unused)) int32_t es_sdl_sound_open(void)
{
    if (es_sdl_voices > 0) return es_sdl_voices;
    if (!es_sdl_init(SDL_INIT_AUDIO)) return 0;
    es_sdl_adev = SDL_OpenAudioDevice(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, NULL);
    if (es_sdl_adev == 0) { fprintf(stderr, "SDL: %s\n", SDL_GetError()); return 0; }
    SDL_AudioSpec spec = { SDL_AUDIO_S8, 1, 8000 };
    for (int i = 0; i < 4; i++)
    {
        es_sdl_vs[es_sdl_voices] = SDL_CreateAudioStream(&spec, NULL);
        if (es_sdl_vs[es_sdl_voices] == NULL) continue;
        if (!SDL_BindAudioStream(es_sdl_adev, es_sdl_vs[es_sdl_voices])) { SDL_DestroyAudioStream(es_sdl_vs[es_sdl_voices]); continue; }
        SDL_SetAudioStreamGetCallback(es_sdl_vs[es_sdl_voices], es_sdl_feed, &es_sdl_voice[es_sdl_voices]);
        es_sdl_voices++;
    }
    if (es_sdl_voices == 0) { SDL_CloseAudioDevice(es_sdl_adev); es_sdl_adev = 0; return 0; }
    SDL_ResumeAudioDevice(es_sdl_adev);
    return es_sdl_voices;
}

static __attribute__((unused)) void es_sdl_sound_play(int32_t voice, const uint8_t *data, int32_t len, int32_t rate, int32_t volume, int32_t cycles)
{
    if (voice < 0 || voice >= es_sdl_voices) return;
    SDL_AudioStream *st = es_sdl_vs[voice];
    SDL_AudioSpec spec = { SDL_AUDIO_S8, 1, rate > 0 ? rate : 8000 };
    SDL_LockAudioStream(st);
    SDL_ClearAudioStream(st);
    SDL_SetAudioStreamFormat(st, &spec, NULL);
    SDL_SetAudioStreamGain(st, (float)volume / 64.0f);
    EsSdlVoice *v = &es_sdl_voice[voice];
    v->data = (const int8_t *)data;
    v->len = len;
    v->pos = 0;
    v->cycles = cycles;
    v->active = len > 0;
    SDL_UnlockAudioStream(st);
}

static __attribute__((unused)) bool es_sdl_sound_busy(int32_t voice)
{
    if (voice < 0 || voice >= es_sdl_voices) return false;
    SDL_AudioStream *st = es_sdl_vs[voice];
    SDL_LockAudioStream(st);
    bool busy = es_sdl_voice[voice].active || SDL_GetAudioStreamQueued(st) > 0;
    SDL_UnlockAudioStream(st);
    return busy;
}

static __attribute__((unused)) void es_sdl_sound_stop(int32_t voice)
{
    if (voice < 0 || voice >= es_sdl_voices) return;
    SDL_AudioStream *st = es_sdl_vs[voice];
    SDL_LockAudioStream(st);
    es_sdl_voice[voice].active = false;
    SDL_ClearAudioStream(st);
    SDL_UnlockAudioStream(st);
}

static __attribute__((unused)) void es_sdl_sound_close(void)
{
    for (int i = 0; i < es_sdl_voices; i++) { es_sdl_sound_stop(i); SDL_DestroyAudioStream(es_sdl_vs[i]); es_sdl_vs[i] = NULL; }
    es_sdl_voices = 0;
    if (es_sdl_adev != 0) SDL_CloseAudioDevice(es_sdl_adev);
    es_sdl_adev = 0;
}
