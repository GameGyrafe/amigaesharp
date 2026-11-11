#!/usr/bin/env bash
# Smoke test for the esharp-sdl image: a statically linked SDL 3 program creates a window, a renderer and a
# streaming texture with the offscreen video driver, draws and reads a pixel back, and plays a short audio
# stream into the dummy audio device. Usage: docker/sdl/smoke-test.sh [image]   (default esharp-sdl)
set -euo pipefail
IMAGE="${1:-esharp-sdl}"
docker run --rm -i "$IMAGE" bash -s <<'INNER'
set -euo pipefail
cd "$(mktemp -d)"
cat > t.c <<'C'
#include <SDL3/SDL.h>
#include <stdio.h>

static int fail(const char *what) { printf("FAIL %s: %s\n", what, SDL_GetError()); return 1; }

int main(void) {
    if (!SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO)) return fail("SDL_Init");
    printf("ok SDL %d.%d.%d, video=%s, audio=%s\n", SDL_MAJOR_VERSION, SDL_MINOR_VERSION, SDL_MICRO_VERSION,
           SDL_GetCurrentVideoDriver(), SDL_GetCurrentAudioDriver());
    SDL_Window *win; SDL_Renderer *ren;
    if (!SDL_CreateWindowAndRenderer("smoke", 64, 48, 0, &win, &ren)) return fail("window+renderer");
    SDL_Texture *tex = SDL_CreateTexture(ren, SDL_PIXELFORMAT_ARGB8888, SDL_TEXTUREACCESS_STREAMING, 64, 48);
    if (!tex) return fail("texture");
    static Uint32 pix[64 * 48];
    for (int i = 0; i < 64 * 48; i++) pix[i] = 0xFF204060u;
    pix[10 * 64 + 20] = 0xFFFF8000u;
    if (!SDL_UpdateTexture(tex, NULL, pix, 64 * 4)) return fail("update texture");
    SDL_RenderClear(ren);
    SDL_RenderTexture(ren, tex, NULL, NULL);
    SDL_Surface *s = SDL_RenderReadPixels(ren, NULL);
    if (!s) return fail("read pixels");
    SDL_Surface *c = SDL_ConvertSurface(s, SDL_PIXELFORMAT_ARGB8888);
    if (!c) return fail("convert");
    Uint32 a = ((Uint32 *)c->pixels)[10 * (c->pitch / 4) + 20], b = ((Uint32 *)c->pixels)[0];
    if (a != 0xFFFF8000u || b != 0xFF204060u) { printf("FAIL pixels %08x %08x\n", a, b); return 1; }
    printf("ok drew and read back pixels\n");
    SDL_AudioSpec spec = { SDL_AUDIO_S16, 1, 22050 };
    SDL_AudioStream *st = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, NULL, NULL);
    if (!st) return fail("open audio");
    static Sint16 samples[2205];
    for (int i = 0; i < 2205; i++) samples[i] = (i / 25 % 2) ? 8000 : -8000;
    if (!SDL_ResumeAudioStreamDevice(st)) return fail("resume audio");
    if (!SDL_PutAudioStreamData(st, samples, sizeof samples)) return fail("put audio");
    SDL_FlushAudioStream(st);
    for (int i = 0; i < 100 && SDL_GetAudioStreamQueued(st) > 0; i++) SDL_Delay(10);
    printf("ok audio stream played (%d bytes still queued)\n", SDL_GetAudioStreamQueued(st));
    SDL_DestroyAudioStream(st); SDL_DestroySurface(c); SDL_DestroySurface(s);
    SDL_DestroyTexture(tex); SDL_DestroyRenderer(ren); SDL_DestroyWindow(win); SDL_Quit();
    printf("ok smoke test passed\n");
    return 0;
}
C
gcc -O2 -Wall -Werror t.c -o t $(pkg-config --cflags --libs --static sdl3)
# statically linked SDL: the binary must not depend on libSDL3.so
if ldd t | grep -qi sdl3; then echo "FAIL: binary links SDL3 dynamically"; exit 1; fi
echo "ok statically linked (no libSDL3.so)"
# the plain pkg-config line (no --static) must compile and link too (only libSDL3.a is installed, so it is static as well)
gcc -O2 t.c -o t-dyn $(pkg-config --cflags --libs sdl3)
echo "ok plain pkg-config line compiles"
SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy ./t
INNER
