using System.Runtime.InteropServices;
using System.Collections.Generic;
using ESharp.Target.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ESharp.Compiler.Tests.TestHelpers;

namespace ESharp.Compiler.Tests;

/// <summary>
/// MUI wrappers on the BOOPSI core (issue #40, part 2): the muimaster.library calls the emitter writes per target (no MUI header),
/// samples/mui on dotnet and, as generated C, against a fake muimaster.library under the sanitizers (each object disposed once, by
/// its parent; no leak), the overrides of library classes in their vtables, and setter properties in object initializers.
/// </summary>
public class MuiTests
{
    private static CompileOptions Defines(params string[] d) => new() { Defines = d };

    // The host gcc calls hooks as plain C functions (hook, object, message), as AROS does; its pointers are 64 bits.
    private static readonly CompileOptions HostC = Defines("AMIGA", "X86_64", "AROS", "ESHARP_HOSTC");

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "mui", "Mui.cs");

    private static string ClassSamplePath => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "muiclass", "MuiClass.cs");

    /// <summary>
    /// A fake muimaster.library (and the bits of intuition, utility and exec the wrappers call): objects with an attribute store, children
    /// (OM_ADDMEMBER, a window's root, a listview's list), MUIM_Notify with MUIM_CallHook run from SetAttrsA as MUI does, a string list,
    /// MUI_DisposeObject that disposes the children first. Text and string contents are copied, as MUI does. Lifecycle lines on stdout.
    /// </summary>
    private const string MuiShim = """
#pragma once
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
/* A BOOPSI class as far as the generated C reads it (its dispatcher hook, cl_Super, cl_InstOffset) and a MUI custom class's own. */
struct Hook { void *h_Succ, *h_Pred; uintptr_t (*h_Entry)(void *, void *, void *); void *h_SubEntry; void *h_Data; };
typedef struct IClass { struct Hook cl_Dispatcher; struct IClass *cl_Super; uint16_t cl_InstOffset; int inst_size, objects; char super[16]; } Class;
typedef struct MObj MObj;
/* Every fake object starts like a MUI area object (Zune headers): struct MUI_NotifyData (seven pointer-sized fields), then the start of
   struct MUI_AreaData (render info, background, font, MUI_MinMax, the box, addleft/addtop/subwidth/subheight, flags). */
struct MObj
{
    void *notify[7];
    void *mad_RenderInfo, *mad_Background, *mad_Font;
    int16_t mad_MinMax[6], mad_Box[4];
    int8_t mad_add[4];
    uint32_t mad_Flags;
    char cls[24]; int n; uintptr_t tag[40], val[40]; MObj *kids[32]; int nkids; uintptr_t note[16][3]; int nnotes; char *items[16]; int nitems;
    Class *custom; int shown;
};
static __attribute__((unused)) int shim_made, shim_disposed, shim_changes, shim_quit, shim_classes, shim_draws, shim_redraws;
static __attribute__((unused)) int shim_is_text(uintptr_t t) { return t == 0x8042f8dcu || t == 0x80428ffdu; }
static __attribute__((unused)) MObj *shim_new(const char *cls, const uintptr_t *tags, int extra);
static __attribute__((unused)) void shim_store(MObj *o, uintptr_t t, uintptr_t v)
{
    int i;
    for (i = 0; i < o->n; i++) if (o->tag[i] == t) break;
    if (i == o->n) { if (o->n == 40) return; o->n++; o->tag[i] = t; o->val[i] = 0; }
    if (shim_is_text(t)) { free((char *)o->val[i]); v = v ? (uintptr_t)strdup((const char *)v) : 0; }
    o->val[i] = v;
    if (t == 0x8042cba5u || t == 0x8042bcceu) o->kids[o->nkids++] = (MObj *)v;   /* a window's root, a listview's list */
}
static __attribute__((unused)) void shim_fire(MObj *o, uintptr_t t, uintptr_t v)
{
    for (int k = 0; k < o->nnotes; k++)
        if (o->note[k][0] == t && (o->note[k][1] == 0x49893131u || o->note[k][1] == v))
        {
            uintptr_t parms[1] = { v };
            void **hook = (void **)o->note[k][2];
            ((uintptr_t (*)(void *, void *, void *))hook[2])(hook, o, parms);   /* h_Entry after the MinNode, as CallHookPkt */
        }
}
static __attribute__((unused)) void *MUI_NewObjectA(const void *cls, void *tags)
{
    static const char *known[] = { "Application.mui", "Window.mui", "Group.mui", "Text.mui", "String.mui", "Cycle.mui", "Slider.mui",
        "Gauge.mui", "Rectangle.mui", "List.mui", "Listview.mui", NULL };
    int k = 0;
    while (known[k] != NULL && strcmp(known[k], (const char *)cls) != 0) k++;
    return known[k] == NULL ? NULL : shim_new(known[k], tags, 0);
}
static __attribute__((unused)) MObj *shim_new(const char *cls, const uintptr_t *tags, int extra)
{
    MObj *o = calloc(1, sizeof(MObj) + (size_t)extra);
    snprintf(o->cls, sizeof o->cls, "%s", cls);
    for (; tags != NULL && tags[0] != 0; tags += 2) shim_store(o, tags[0], tags[1]);
    shim_made++;
    return o;
}
static __attribute__((unused)) void *MUI_MakeObjectA(int32_t type, uintptr_t *parms)
{
    char cls[24];
    snprintf(cls, sizeof cls, "Make%d", (int)type);
    MObj *o = shim_new(cls, NULL, 0);
    if (type == 2 && parms[0] != 0) shim_store(o, 0x8042f8dcu, parms[0]);   /* a button shows its label */
    return o;
}
static __attribute__((unused)) uintptr_t shim_dispatch(MObj *o, void *msg) { return o->custom->cl_Dispatcher.h_Entry(o->custom, o, msg); }
/* struct MUI_EventHandlerNode as the AROS Zune header defines it (a MinNode, two BYTEs, a UWORD, object, class, IDCMP flags), compiled
   by the host gcc: the generated C writes it at the offsets of Layout.g.cs (AROS x86-64), so the two must agree. A window's handler
   list, in the order they were added; MUIM_HandleEvent goes to each handler that wants the event's class until one eats it. */
struct ShimEhn { void *succ, *pred; int8_t reserved, priority; uint16_t flags; void *object; Class *cls; uint32_t events; };
static __attribute__((unused)) struct ShimEhn *shim_ehn[16];
static __attribute__((unused)) int shim_nehn, shim_ehn_added, shim_ehn_removed, shim_events_eaten, shim_events_sent;
/* The first node each object added: E# keeps one node per object from MUIM_Setup to MUIM_Cleanup and changes its events in place, so a
   later add of another node by the same object (in one setup: these tests open the window once) means a node was freed and replaced. */
static __attribute__((unused)) void *shim_ehn_obj[16], *shim_ehn_first[16];
static __attribute__((unused)) int shim_nehn_obj;
static __attribute__((unused)) void shim_event(uint32_t cls, uint16_t code, int16_t x, int16_t y)
{
    /* An IntuiMessage of AROS x86-64 (Layout.g.cs): Class at 48, Code 52, Qualifier 54, MouseX 64, MouseY 66. */
    uint8_t im[96] = { 0 };
    memcpy(im + 48, &cls, 4); memcpy(im + 52, &code, 2); memcpy(im + 64, &x, 2); memcpy(im + 66, &y, 2);
    uintptr_t m[3] = { 0x80426d66u, (uintptr_t)im, (uintptr_t)(intptr_t)-1 };
    for (int k = 0; k < shim_nehn; k++)
    {
        struct ShimEhn *n = shim_ehn[k];
        if ((n->events & cls) == 0) continue;
        shim_events_sent++;
        if (n->cls->cl_Dispatcher.h_Entry(n->cls, n->object, m) & 1) { shim_events_eaten++; printf("[event %u,%u at %d,%d eaten by handler %d]\n", (unsigned)cls, (unsigned)code, x, y, k); return; }
    }
    printf("[event %u,%u at %d,%d not eaten]\n", (unsigned)cls, (unsigned)code, x, y);
}
static __attribute__((unused)) void MUI_DisposeObject(void *obj);
/* The object goes (OM_DISPOSE of the superclass, or MUI_DisposeObject of a plain one): its children first, as MUI does. */
static __attribute__((unused)) void shim_dispose_raw(MObj *o)
{
    for (int k = 0; k < o->nkids; k++) MUI_DisposeObject(o->kids[k]);
    for (int i = 0; i < o->n; i++) if (shim_is_text(o->tag[i])) free((char *)o->val[i]);
    for (int i = 0; i < o->nitems; i++) free(o->items[i]);
    printf("[dispose %s%s]\n", o->custom ? "custom " : "", o->cls);
    shim_disposed++;
    free(o);
}
static __attribute__((unused)) void MUI_DisposeObject(void *obj)
{
    MObj *o = obj;
    if (o->custom != NULL) { uintptr_t m[1] = { 0x102 }; shim_dispatch(o, m); return; }   /* OM_DISPOSE through the class's dispatcher */
    shim_dispose_raw(o);
}
/* MUI_CreateCustomClass: a struct MUI_CustomClass whose seventh pointer (mcc_Class) is a class with the dispatcher; instance data after the object. */
static __attribute__((unused)) void *MUI_CreateCustomClass(void *base, const void *super, void *supermcc, int32_t size, void *dispatcher)
{
    if (base != NULL || supermcc != NULL || size <= 0) return NULL;
    void **mcc = calloc(8, sizeof(void *));
    Class *cl = calloc(1, sizeof(Class));
    cl->cl_Dispatcher.h_Entry = (uintptr_t (*)(void *, void *, void *))dispatcher;
    cl->cl_InstOffset = (uint16_t)sizeof(MObj);
    cl->inst_size = (int)size;
    snprintf(cl->super, sizeof cl->super, "%s", (const char *)super);
    mcc[6] = cl;
    shim_classes++;
    printf("[create class on %s, %d bytes of instance data]\n", cl->super, (int)size);
    return mcc;
}
static __attribute__((unused)) int32_t MUI_DeleteCustomClass(void *mcc)
{
    Class *cl = ((Class **)mcc)[6];
    if (cl->objects != 0) { printf("[class on %s still has %d objects]\n", cl->super, cl->objects); return 0; }
    printf("[delete class on %s]\n", cl->super);
    shim_classes--;
    free(cl);
    free(mcc);
    return 1;
}
static __attribute__((unused)) void MUI_Redraw(void *obj, uint32_t flags)
{
    MObj *o = obj;
    if (o->custom == NULL || !o->shown) { printf("[redraw of an object that is not shown]\n"); return; }
    uintptr_t m[2] = { 0x80426f3fu, flags };
    shim_redraws++;
    shim_dispatch(o, m);
}
static __attribute__((unused)) int32_t MUI_RequestA(void *app, void *win, uint32_t flags, const void *title, const void *gadgets, const void *format, void *parms)
{ (void)app; (void)win; (void)flags; (void)title; (void)gadgets; (void)format; (void)parms; return 1; }
static __attribute__((unused)) int32_t MUI_Error(void) { return 0; }
static __attribute__((destructor)) void shim_report(void)
{
    printf("[shim: %d made, %d disposed, %d group changes]\n", shim_made, shim_disposed, shim_changes);
    if (shim_draws > 0 || shim_classes != 0) printf("[shim: %d draws, %d redraws, %d classes left]\n", shim_draws, shim_redraws, shim_classes);
    if (shim_ehn_added > 0) printf("[shim: %d event handlers added, %d removed, %d left; %d events sent, %d eaten]\n", shim_ehn_added, shim_ehn_removed, shim_nehn, shim_events_sent, shim_events_eaten);
}
""";

    private const string IntuitionShim = """
#pragma once
#include <stdio.h>
#include <stdint.h>
typedef void Object;
typedef void *Msg;
struct Library;
static __attribute__((unused)) unsigned char shim_lib[64];
static inline __attribute__((unused)) void *OpenLibrary(const void *n, uint32_t v) { (void)v; return strcmp((const char *)n, "muimaster.library") == 0 && getenv("SHIM_NO_MUI") ? NULL : shim_lib; }
static inline __attribute__((unused)) void CloseLibrary(void *l) { (void)l; printf("[close library]\n"); }
static inline __attribute__((unused)) uint32_t Wait(uint32_t s) { return s; }
static inline __attribute__((unused)) void Delay(int32_t t) { (void)t; }
/* NewObjectA of a private class (a MUI custom class): OM_NEW through its dispatcher, with the class as the object, as BOOPSI does. */
static inline __attribute__((unused)) void *NewObjectA(void *cl, const void *id, void *tags)
{
    (void)id;
    if (cl == NULL) return NULL;
    uintptr_t m[3] = { 0x101, (uintptr_t)tags, 0 };
    return (void *)((Class *)cl)->cl_Dispatcher.h_Entry(cl, cl, m);
}
static inline __attribute__((unused)) void DisposeObject(void *o) { (void)o; printf("[intuition DisposeObject of a MUI object]\n"); }
/* What MUI does when a window opens (setup, size, layout, show, draw each custom object) or closes (hide, cleanup). */
static struct { void *wo, *screen, *dri; uint16_t *pens; void *window, *rp; uint32_t flags; } shim_mri;
static uint16_t shim_pens[8] = { 2, 2, 0, 1, 1, 1, 3, 2 };
static int shim_rp;
static __attribute__((unused)) void shim_show_tree(MObj *o, int on)
{
    for (int k = 0; k < o->nkids; k++) shim_show_tree(o->kids[k], on);
    if (o->custom == NULL) return;
    if (on && !o->shown)
    {
        shim_mri.pens = shim_pens;
        shim_mri.rp = &shim_rp;
        uintptr_t setup[2] = { 0x80428354u, (uintptr_t)&shim_mri };
        if (!shim_dispatch(o, setup)) { printf("[setup refused]\n"); return; }
        int16_t mm[6] = { 0 };
        uintptr_t ask[2] = { 0x80423874u, (uintptr_t)mm };
        shim_dispatch(o, ask);
        if (mm[0] > mm[4] || mm[4] > mm[2] || mm[1] > mm[5] || mm[5] > mm[3]) printf("[bad min/max: %d %d %d %d %d %d]\n", mm[0], mm[1], mm[2], mm[3], mm[4], mm[5]);
        o->mad_Box[0] = 10; o->mad_Box[1] = 20; o->mad_Box[2] = mm[4]; o->mad_Box[3] = mm[5];
        o->mad_add[0] = 2; o->mad_add[1] = 2; o->mad_add[2] = 4; o->mad_add[3] = 4;
        uintptr_t show[1] = { 0x8042cc84u };
        shim_dispatch(o, show);
        uintptr_t draw[2] = { 0x80426f3fu, 1 };
        shim_dispatch(o, draw);
    }
    else if (!on && o->shown)
    {
        uintptr_t hide[1] = { 0x8042f20fu }, cleanup[1] = { 0x8042d985u };
        shim_dispatch(o, hide);
        shim_dispatch(o, cleanup);
    }
}
static __attribute__((unused)) void shim_set_raw(MObj *o, const uintptr_t *tags)
{
    const uintptr_t *t = tags;
    for (; t != NULL && t[0] != 0; t += 2) shim_store(o, t[0], t[1]);
    for (t = tags; t != NULL && t[0] != 0; t += 2) shim_fire(o, t[0], t[1]);
    for (t = tags; t != NULL && t[0] != 0; t += 2) if (t[0] == 0x80428aa0u) { shim_mri.wo = o; for (int k = 0; k < o->nkids; k++) shim_show_tree(o->kids[k], t[1] != 0); }
}
static inline __attribute__((unused)) uintptr_t SetAttrsA(void *obj, void *tags)
{
    MObj *o = obj;
    if (o->custom != NULL) { uintptr_t m[3] = { 0x103, (uintptr_t)tags, 0 }; return shim_dispatch(o, m); }   /* OM_SET */
    shim_set_raw(o, tags);
    return 0;
}
static inline __attribute__((unused)) uintptr_t SetGadgetAttrsA(void *g, void *w, void *r, void *t) { (void)w; (void)r; return SetAttrsA(g, t); }
static inline __attribute__((unused)) void RefreshGList(void *g, void *w, void *r, int32_t n) { (void)g; (void)w; (void)r; (void)n; }
static inline __attribute__((unused)) uint16_t RemoveGList(void *w, void *g, int32_t n) { (void)w; (void)g; (void)n; return 0; }
static inline __attribute__((unused)) uintptr_t shim_get_raw(uint32_t id, MObj *o, void *store)
{
    if (id == 0x80421654u) { *(uintptr_t *)store = (uintptr_t)o->nitems; return 1; }
    for (int i = 0; i < o->n; i++) if (o->tag[i] == id) { *(uintptr_t *)store = o->val[i]; return 1; }
    return 0;
}
static inline __attribute__((unused)) uintptr_t GetAttr(uint32_t id, void *obj, void *store)
{
    MObj *o = obj;
    if (o->custom != NULL) { uintptr_t m[3] = { 0x104, id, (uintptr_t)store }; return shim_dispatch(o, m); }   /* OM_GET */
    return shim_get_raw(id, o, store);
}
""";

    private const string AlibShim = """
#pragma once
/* DoMethodA for the fake MUI objects; a custom object's methods go to its class's dispatcher. */
static __attribute__((unused)) uintptr_t shim_method(MObj *o, uintptr_t *m)
{
    switch (m[0])
    {
        case 260: return shim_get_raw((uint32_t)m[1], o, (void *)m[2]);
        case 0x109: o->kids[o->nkids++] = (MObj *)m[1]; return 0;
        case 0x10a:
            for (int k = 0; k < o->nkids; k++)
                if (o->kids[k] == (MObj *)m[1]) { o->kids[k] = o->kids[--o->nkids]; break; }
            return 0;
        case 0x8042c9cbu:
            if (m[5] != 0x8042b96bu || o->nnotes == 16) return 0;
            o->note[o->nnotes][0] = m[1]; o->note[o->nnotes][1] = m[2]; o->note[o->nnotes][2] = m[6]; o->nnotes++;
            return 0;
        case 0x80423ba6u:                                                                 /* MUIM_Application_NewInput */
            *(uint32_t *)m[1] = 0;
            if (getenv("SHIM_EVENTS") && !shim_quit && shim_nehn > 0)
            {
                /* A user: a click in the middle of the graphs' contents (the fake lays out every custom object at 10,20 with 2 pixels
                   of frame: contents from 12,22, 192 x 64), a drag to the right edge, the button up, a click outside, then quit. */
                shim_event(8, 0x68, 12 + 96, 22 + 32);
                shim_event(16, 0, 12 + 191, 22 + 32);
                shim_event(8, 0xe8, 12 + 191, 22 + 32);
                shim_event(8, 0x68, 2, 2);
                shim_event(8, 0xe8, 2, 2);
                shim_event(1024, 0x20, 12, 22);
                shim_quit = 1;
            }
            return shim_quit ? 0xffffffffu : 0;
        case 0x804203b7u:                                                                 /* MUIM_Window_AddEventHandler */
            if (o != shim_mri.wo || shim_nehn == 16) { printf("[event handler added to the wrong object]\n"); return 0; }
            shim_ehn[shim_nehn++] = (struct ShimEhn *)m[1]; shim_ehn_added++;
            {
                int j = 0;
                while (j < shim_nehn_obj && shim_ehn_obj[j] != shim_ehn[shim_nehn - 1]->object) j++;
                if (j == shim_nehn_obj && j < 16) { shim_ehn_obj[j] = shim_ehn[shim_nehn - 1]->object; shim_ehn_first[j] = (void *)m[1]; shim_nehn_obj++; }
                else if (j < shim_nehn_obj && shim_ehn_first[j] != (void *)m[1]) printf("[event handler node replaced]\n");
            }
            return 0;
        case 0x8042679eu:                                                                 /* MUIM_Window_RemEventHandler */
            for (int k = 0; k < shim_nehn; k++)
                if (shim_ehn[k] == (struct ShimEhn *)m[1]) { for (; k < shim_nehn - 1; k++) shim_ehn[k] = shim_ehn[k + 1]; shim_nehn--; shim_ehn_removed++; return 0; }
            printf("[unknown event handler removed]\n");
            return 0;
        case 0x804276efu: shim_quit = 1; return 0;
        case 0x804254d5u: if (o->nitems < 16) o->items[o->nitems++] = strdup((const char *)m[1]); return 0;
        case 0x8042ad89u: for (int i = 0; i < o->nitems; i++) free(o->items[i]); o->nitems = 0; return 0;
        case 0x804280ecu: { intptr_t i = (intptr_t)m[1]; *(void **)m[2] = i >= 0 && i < o->nitems ? o->items[i] : NULL; return 0; }
        case 0x80420887u: case 0x8042d1ccu: shim_changes++; return 0;
    }
    return 0;
}
static __attribute__((unused)) uintptr_t DoMethodA(void *obj, void *msg)
{
    MObj *o = obj;
    if (o->custom != NULL) return shim_dispatch(o, msg);
    return shim_method(o, msg);
}
/* The superclass of a custom class (Area.mui, Group.mui): what MUI's classes do with the methods a dispatcher passes on. */
static __attribute__((unused)) uintptr_t DoSuperMethodA(Class *cl, void *obj, void *msg)
{
    MObj *o = obj;
    uintptr_t *m = msg;
    switch (m[0])
    {
        case 0x101:
        {
            if (obj != cl) { printf("[OM_NEW without the class as its object]\n"); return 0; }
            MObj *n = shim_new(cl->super, (const uintptr_t *)m[1], cl->inst_size);
            n->custom = cl;
            cl->objects++;
            return (uintptr_t)n;
        }
        case 0x102: cl->objects--; shim_dispose_raw(o); return 0;
        case 0x103: shim_set_raw(o, (const uintptr_t *)m[1]); return 1;
        case 0x104: return shim_get_raw((uint32_t)m[1], o, (void *)m[2]);
        case 0x80428354u: o->mad_RenderInfo = (void *)m[1]; return 1;                         /* MUIM_Setup */
        case 0x8042d985u: o->mad_RenderInfo = NULL; return 1;                                 /* MUIM_Cleanup */
        case 0x8042cc84u: o->shown = 1; return 1;                                             /* MUIM_Show */
        case 0x8042f20fu: o->shown = 0; return 1;                                             /* MUIM_Hide */
        case 0x80423874u: { int16_t *mm = (int16_t *)m[1]; for (int i = 0; i < 6; i++) mm[i] = 4; return 0; }   /* the frame */
        case 0x80426f3fu: if (!o->shown || o->mad_RenderInfo == NULL) printf("[draw while not shown]\n"); shim_draws++; return 0;
    }
    return shim_method(o, m);
}
""";

    private static Dictionary<string, string> Shims() => new()
    {
        ["es_muimaster_shim.h"] = MuiShim,
        ["proto/intuition.h"] = "#pragma once\n#include <es_muimaster_shim.h>\n" + IntuitionShim,
        ["proto/utility.h"] = "#pragma once\n",
        ["clib/alib_protos.h"] = "#pragma once\n#include <proto/intuition.h>\n" + AlibShim,
        ["intuition/classes.h"] = "#pragma once\n#include <proto/intuition.h>\n",
        ["proto/graphics.h"] = GraphicsShim,
    };

    /// <summary>The graphics.library calls of <c>MuiRender</c>: rectangles must be the right way round (clipped), and are counted.</summary>
    private const string GraphicsShim = """
#pragma once
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
static __attribute__((unused)) int shim_fills, shim_lines, shim_texts, shim_pen;
static inline __attribute__((unused)) void SetAPen(void *rp, int32_t pen) { (void)rp; shim_pen = pen; }
static inline __attribute__((unused)) void SetDrMd(void *rp, int32_t mode) { (void)rp; (void)mode; }
static inline __attribute__((unused)) void SetFont(void *rp, void *font) { (void)rp; (void)font; }
static inline __attribute__((unused)) void RectFill(void *rp, int32_t x0, int32_t y0, int32_t x1, int32_t y1)
{ if (rp == NULL || x1 < x0 || y1 < y0) { printf("[bad RectFill %d,%d %d,%d]\n", (int)x0, (int)y0, (int)x1, (int)y1); abort(); } shim_fills++; }
static inline __attribute__((unused)) void Move(void *rp, int32_t x, int32_t y) { (void)rp; (void)x; (void)y; }
static inline __attribute__((unused)) void Draw(void *rp, int32_t x, int32_t y) { (void)rp; (void)x; (void)y; shim_lines++; }
static inline __attribute__((unused)) void Text(void *rp, const char *s, int32_t n) { (void)rp; if ((int32_t)strlen(s) != n) abort(); shim_texts++; }
static __attribute__((destructor)) void shim_gfx_report(void) { if (shim_fills > 0) printf("[gfx: %d fills, %d lines, %d texts]\n", shim_fills, shim_lines, shim_texts); }
""";

    private static (int, string) RunShimmed(string c) => HasSanitizers ? RunSanitized(c, Shims()) : RunWithHostGcc(c, Shims());

    [Fact]
    public void Mui_sample_runs_against_a_fake_muimaster_with_each_object_disposed_once_and_no_leak()
    {
        if (!HasGcc) return;
        var c = CompileOk(File.ReadAllText(SamplePath), HostC);
        Assert.Contains("#include <es_muimaster_shim.h>", c);
        Assert.DoesNotContain("proto/muimaster.h", c);
        Assert.DoesNotContain("libraries/mui.h", c);
        var (code, output) = RunShimmed(c);
        Assert.True(code == 0, output);
        Assert.DoesNotContain("FAIL", output);
        Assert.Contains("mui: all ok\n", output);
        // MUI disposes the tree: never intuition's DisposeObject. The lone Text and Slider, the object taken out of the open window (a
        // group told before and after, twice) and the application with the 19 objects in it: 23 made (the unknown class is not), 23 disposed.
        Assert.DoesNotContain("[intuition DisposeObject", output);
        Assert.Contains("[shim: 23 made, 23 disposed, 4 group changes]", output);
        // The application goes last, after its window, which goes after its root group (children first).
        var tree = output[output.IndexOf("ok the window closes\n", StringComparison.Ordinal)..];
        Assert.True(tree.IndexOf("[dispose Group.mui]", StringComparison.Ordinal) < tree.IndexOf("[dispose Window.mui]", StringComparison.Ordinal)
                    && tree.IndexOf("[dispose Window.mui]", StringComparison.Ordinal) < tree.IndexOf("[dispose Application.mui]", StringComparison.Ordinal), tree);
        Assert.Contains("[close library]", output);
    }

    [Fact]
    public void Mui_sample_says_skip_without_muimaster()
    {
        if (!HasGcc) return;
        var c = CompileOk(File.ReadAllText(SamplePath), HostC);
        Environment.SetEnvironmentVariable("SHIM_NO_MUI", "1");
        try
        {
            var (code, output) = RunWithHostGcc(c, Shims());
            Assert.True(code == 0, output);
            Assert.StartsWith("skip: MUI not installed\n", output);
            Assert.DoesNotContain("ok ", output);
        }
        finally { Environment.SetEnvironmentVariable("SHIM_NO_MUI", null); }
    }

    [Theory]
    [InlineData(false, "mui: all ok\n")]
    [InlineData(true, "events: 1 Hello press(es), 2 slider change(s), last 70, 1 Return(s), list entry 1\n")]
    public void Mui_sample_runs_the_same_on_dotnet(bool wait, string expected)
    {
        var src = File.ReadAllText(SamplePath);
        var compilation = CSharpCompilation.Create("mui", new[] { CSharpSyntaxTree.ParseText(src) }, PlatformSubclassTests.HostReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        if (wait) ESharp.Platform.Args.HostSet("WAIT");
        try
        {
            using var console = HostConsole.Capture();
            asm.GetType("MuiSample.Program")!.GetMethod("Main")!.Invoke(null, null);
            Assert.DoesNotContain("FAIL", console.Output);
            Assert.Contains(expected, console.Output);
            Assert.EndsWith("mui: all ok\n", console.Output);
        }
        finally { ESharp.Platform.Args.HostReset(); }
    }

    [Theory]
    [InlineData("m68k", "jsr -30(%%a6)")]
    [InlineData("aros", "AROS_LC2(APTR, MUI_NewObjectA")]
    [InlineData("aros-i386", "AROS_LC2(APTR, MUI_NewObjectA")]
    [InlineData("os4", "(IExec->EmulateTags)((APTR)((uint8_t *)es_MUIMasterBase - (lvo))")]
    [InlineData("morphos", "(*MyEmulHandle->EmulCallDirectOS)(-(lvo))")]
    public void Mui_sample_compiles_for_every_amiga_target_without_a_MUI_header(string target, string call)
    {
        var r = EsCompiler.Compile(new[] { new SourceFile("Mui.cs", File.ReadAllText(SamplePath)) }, Targets.Find(target)!.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains(call, r.C!);
        Assert.Contains("#define MUIMasterBase es_MUIMasterBase", r.C!);
        Assert.DoesNotContain("muimaster.h", r.C!);
        Assert.DoesNotContain("libraries/mui.h", r.C!);
        Assert.Contains("Es_ESharp_Platform_MuiNotification_Entry_HOOK", r.C!);
    }

    [Fact]
    public void Mui_vectors_match_the_fd_bias()
    {
        // Facts from the AROS fd of muimaster.library (bias 30, one vector each 6 bytes): NewObjectA 1st, DisposeObject 2nd,
        // RequestA 3rd, Error 7th, MakeObjectA 16th. The C# declarations, the m68k jsr offsets and the AROS call numbers must agree.
        var lvos = new Dictionary<string, int>
        {
            ["MUI_NewObjectA"] = 30, ["MUI_DisposeObject"] = 36, ["MUI_RequestA"] = 42, ["MUI_Error"] = 66, ["MUI_Redraw"] = 102,
            ["MUI_CreateCustomClass"] = 108, ["MUI_DeleteCustomClass"] = 114, ["MUI_MakeObjectA"] = 120,
        };
        var platform = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ESharp.Platform", "Mui.cs"));
        string C(string target) => EsCompiler.Compile(new[] { new SourceFile("Mui.cs", File.ReadAllText(SamplePath)) }, Targets.Find(target)!.ToOptions()).C!;
        string m68k = C("m68k"), aros = C("aros"), morphos = C("morphos");
        foreach (var (name, lvo) in lvos)
        {
            Assert.Matches($@"\[AmigaLib\(""muimaster"", -{lvo}\), Since\(19\)\] public static extern [\w*]+ {name}\(", platform);
            Assert.Contains($"jsr -{lvo}(%%a6)", m68k);
            Assert.Contains($"AROS_LC", aros);
            Assert.Contains($"struct Library *, es_MUIMasterBase, {lvo / 6}, MUIMaster)", aros);
            Assert.Contains($"ES_MUI_CALL({lvo})", morphos);
        }
    }

    [Fact]
    public void Mui_classes_on_sdl_are_Amiga_only()
    {
        var r = Compile(Program("", "var a = new MuiApplication(\"x\", \"X\"); var t = new MuiText(\"y\");"), Targets.Sdl.ToOptions());
        Assert.False(r.Success);
        var messages = r.Diagnostics.Where(d => d.Id == "ES0102").Select(d => d.GetMessage()).ToList();
        Assert.Contains(messages, m => m.Contains("'MuiApplication'") && m.Contains("'sdl'"));
        Assert.Contains(messages, m => m.Contains("'MuiText'"));
    }

    [Fact]
    public void An_override_in_a_library_class_is_in_its_vtable_when_only_the_base_member_is_called()
    {
        // BoopsiObject.Create calls the virtual CreateAll, which MuiObject overrides: nothing names the override, yet it must be in the
        // table of every constructed MUI class (it was missing: a null slot, called on AmigaOS 4 as address 0).
        var c = CompileOk(Program("", "var t = new MuiText(\"x\"); if (Mui.Open() && t.Create()) Dos.PutStr(\"made\\n\"); t.Dispose(); Heap.Free(ref t);"), HostC);
        Assert.Contains("v_ESharp_Platform_BoopsiObject_CreateAll = Es_ESharp_Platform_MuiText__v_ESharp_Platform_BoopsiObject_CreateAll", c);
        Assert.Contains("v_ESharp_Platform_BoopsiObject_DisposeSystem = Es_ESharp_Platform_MuiText__v_ESharp_Platform_BoopsiObject_DisposeSystem", c);
        // A program without MUI classes does not pull them in through BoopsiObject's virtual members.
        var plain = CompileOk(Program("", "var o = new BoopsiObject(\"propgclass\"); o.Create(); o.Dispose(); Heap.Free(ref o);"), HostC);
        Assert.DoesNotContain("es_MUIMasterBase", plain);
        Assert.DoesNotContain("MuiObject_CreateAll(", plain);
    }

    [Fact]
    public void An_object_initializer_runs_property_setters_in_order()
    {
        if (!HasGcc) return;
        var c = CompileOk(Program(@"
class Box
{
    public int Log;
    public int Width { get; set; }
    public int Frame { set { Log = Log * 10 + value; } }
    public string? Title { set { Log = Log * 10 + (value == null ? 0 : value.Length); } }
    public Box(int start) { Log = start; }
}", "var b = new Box(1) { Frame = 2, Width = 7, Title = \"abc\" }; Dos.PutStr($\"{b.Log} {b.Width}\\n\"); Heap.Free(ref b);"));
        var (code, output) = RunWithHostGcc(c);
        Assert.True(code == 0, output);
        Assert.Equal("123 7\n", output);
    }

    // ------------------------------------------------------------------ custom classes (issue #93)

    [Fact]
    public void Muiclass_sample_runs_against_a_fake_muimaster_with_a_C_sharp_dispatcher()
    {
        if (!HasGcc) return;
        var c = CompileOk(File.ReadAllText(ClassSamplePath), HostC);
        // The dispatcher's helpers: DoSuperMethodA (amiga.lib here and on m68k) and the instance data from cl_InstOffset.
        Assert.Contains("DoSuperMethodA((Class *)cl, (Object *)obj, (Msg)msg)", c);
        Assert.Contains("((Class *)cl)->cl_InstOffset", c);
        Assert.Contains("Es_ESharp_Platform_MuiCustomClasses_Dispatch_HOOK", c);
        Assert.DoesNotContain("libraries/mui.h", c);
        var (code, output) = RunShimmed(c);
        Assert.True(code == 0, output);
        Assert.DoesNotContain("FAIL", output);
        Assert.Contains("muiclass: all ok\n", output);
        // One class on Area.mui for both graphs, one on Group.mui, each made once and deleted after the application; the lone graph
        // makes the Area class again. Instance data: one pointer (the C# object).
        Assert.Equal(2, Count(output, "[create class on Area.mui, 8 bytes of instance data]"));
        Assert.Equal(1, Count(output, "[create class on Group.mui, 8 bytes of instance data]"));
        Assert.Equal(2, Count(output, "[delete class on Area.mui]"));
        Assert.Equal(1, Count(output, "[delete class on Group.mui]"));
        Assert.DoesNotContain("still has", output);
        var app = output[output.IndexOf("ok MUIM_Cleanup", StringComparison.Ordinal)..];
        Assert.True(app.IndexOf("[dispose custom Group.mui]", StringComparison.Ordinal) < app.IndexOf("[delete class on Group.mui]", StringComparison.Ordinal), app);
        Assert.True(app.IndexOf("[dispose Application.mui]", StringComparison.Ordinal) < app.IndexOf("[delete class on Area.mui]", StringComparison.Ordinal), app);
        // The application's ten objects, three of them custom, and the lone graph: each made and disposed once; nothing drawn when hidden.
        Assert.Contains("[shim: 10 made, 10 disposed, 0 group changes]", output);
        Assert.Contains("classes left]", output);
        Assert.Contains(" 0 classes left]", output);
        Assert.DoesNotContain("[draw while not shown]", output);
        Assert.DoesNotContain("[redraw of an object that is not shown]", output);
        Assert.DoesNotContain("[bad", output);
        Assert.Matches(@"\[gfx: \d+ fills, \d+ lines, \d+ texts\]", output);
    }

    [Fact]
    public void Muiclass_sample_takes_clicks_and_drags_through_MUIM_HandleEvent_against_the_fake_muimaster()
    {
        if (!HasGcc) return;
        var c = CompileOk(File.ReadAllText(ClassSamplePath), HostC);
        Environment.SetEnvironmentVariable("SHIM_EVENTS", "1");
        try
        {
            var (code, output) = HasSanitizers ? RunSanitized(c, Shims(), "WAIT", "CLICKS") : RunWithHostGcc(c, Shims(), "WAIT", "CLICKS");
            Assert.True(code == 0, output);
            Assert.DoesNotContain("FAIL", output);
            Assert.Contains("muiclass: all ok\n", output);
            // Each graph's node added at MUIM_Setup and removed at MUIM_Cleanup (the group asks for no events); the node's fields as the
            // Zune header has them. The click lands on both graphs' boxes (the fake puts every object at one place): the first handler
            // eats it, the second never sees it, and the first one asks for mouse moves from inside OnEvent: its node is taken out,
            // changed and put back (at the end of the list, after the second graph's). Only it gets the move; the button up goes to the
            // second graph (not dragging, not eaten) and then the first, which eats it and drops the moves (out and back once more).
            // The click outside and its button up go to both and are not eaten; the raw key is not asked for. Every add is the same node
            // per object (never freed and replaced while the window is open).
            Assert.Contains("[shim: 4 event handlers added, 4 removed, 0 left; 8 events sent, 3 eaten]", output);
            Assert.Contains("[event 8,104 at 108,54 eaten by handler 0]", output);
            Assert.Contains("[event 16,0 at 203,54 eaten by handler 1]", output);
            Assert.Contains("[event 8,232 at 203,54 eaten by handler 1]", output);
            Assert.DoesNotContain("[event handler node replaced]", output);
            Assert.Contains("[event 8,104 at 2,2 not eaten]", output);
            Assert.Contains("[event 1024,32 at 12,22 not eaten]", output);
            Assert.Contains("events: level 100, slider 100, 1 click(s), 1 move(s); other graph 20, 0 click(s)\n", output);
            Assert.Contains("ok a click reached a graph (MUIM_HandleEvent)\n", output);
            Assert.DoesNotContain("[event handler added to the wrong object]", output);
            Assert.DoesNotContain("[unknown event handler removed]", output);
        }
        finally { Environment.SetEnvironmentVariable("SHIM_EVENTS", null); }
    }

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    [Theory]
    [InlineData(false, "muiclass: all ok\n")]
    [InlineData(true, "events: level 100, slider 100, 1 click(s), 1 move(s); other graph 20, 0 click(s)\n")]
    public void Muiclass_sample_runs_the_same_on_dotnet(bool wait, string expected)
    {
        var src = File.ReadAllText(ClassSamplePath);
        var compilation = CSharpCompilation.Create("muiclass", new[] { CSharpSyntaxTree.ParseText(src) }, PlatformSubclassTests.HostReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var asm = System.Reflection.Assembly.Load(ms.ToArray());
        if (wait) ESharp.Platform.Args.HostSet("WAIT");
        try
        {
            using var console = HostConsole.Capture();
            asm.GetType("MuiClassSample.Program")!.GetMethod("Main")!.Invoke(null, null);
            Assert.DoesNotContain("FAIL", console.Output);
            Assert.Contains(expected, console.Output);
            Assert.EndsWith("muiclass: all ok\n", console.Output);
        }
        finally { ESharp.Platform.Args.HostReset(); }
    }

    [Theory]
    [InlineData("m68k", "DoSuperMethodA((Class *)cl")]
    [InlineData("aros", "CALLHOOKPKT((struct Hook *)((Class *)cl)->cl_Super, obj, msg)")]
    [InlineData("aros-i386", "CALLHOOKPKT((struct Hook *)((Class *)cl)->cl_Super, obj, msg)")]
    [InlineData("os4", "IDoSuperMethodA((Class *)cl")]
    [InlineData("morphos", "DoSuperMethodA((Class *)cl")]
    public void Muiclass_sample_compiles_for_every_amiga_target_without_a_MUI_header(string target, string superCall)
    {
        var r = EsCompiler.Compile(new[] { new SourceFile("MuiClass.cs", File.ReadAllText(ClassSamplePath)) }, Targets.Find(target)!.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Contains(superCall, r.C!);
        Assert.Contains("MUI_CreateCustomClass(NULL, ", r.C!);
        Assert.Contains("Es_ESharp_Platform_MuiCustomClasses_Dispatch_HOOK", r.C!);
        Assert.DoesNotContain("muimaster.h", r.C!);
        Assert.DoesNotContain("libraries/mui.h", r.C!);
    }

    // Issue #105: Mui.Open opens muiscreen.library first on AROS, with exec's AllocMem patched for its own task so that the stack of
    // muiscreen's handler task (overrun by about 4 KB on hosted AROS) gets memory of its own below it. The guard is in every Amiga
    // target's C (AROS only, under __AROS__); the choice of the request to pad is plain C and runs here with the host compiler.
    [Theory]
    [InlineData("m68k")]
    [InlineData("aros")]
    [InlineData("aros-i386")]
    [InlineData("os4")]
    [InlineData("morphos")]
    public void Mui_open_guards_the_muiscreen_handler_stack_on_aros(string target)
    {
        var r = EsCompiler.Compile(new[] { new SourceFile("Mui.cs", File.ReadAllText(SamplePath)) }, Targets.Find(target)!.ToOptions());
        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        string c = r.C!.Replace("\r\n", "\n");
        int begin = c.IndexOf("    es_mui_guard_begin();\n", StringComparison.Ordinal);
        int open = c.IndexOf("OpenLibrary(\"muimaster.library\", 19)", StringComparison.Ordinal);
        int end = c.IndexOf("    es_mui_guard_end();\n", StringComparison.Ordinal);
        Assert.True(begin >= 0 && begin < open && open < end, "guard begin, OpenLibrary(muimaster), guard end in Mui.Open");
        int aros = c.IndexOf("#if defined(__AROS__)\n#include <proto/exec.h>\n#include <exec/memory.h>", StringComparison.Ordinal);
        Assert.True(aros >= 0, "the AROS guard");
        string guard = c.Substring(aros, c.IndexOf("#else\n", aros, StringComparison.Ordinal) - aros);
        Assert.Contains("SetFunction((struct Library *)SysBase, -33 * (LONG)LIB_VECTSIZE, (APTR)stub)", guard);
        Assert.Contains("OpenLibrary((CONST_STRPTR)\"muiscreen.library\", 0)", guard);
        Assert.Contains("FindTask(NULL) == es_muis_task", guard);
        if (!HasGcc) return;
        int pad = c.IndexOf("static __attribute__((unused)) int es_muis_pad(", StringComparison.Ordinal);
        string padFn = c.Substring(pad, c.IndexOf("\n}\n", pad, StringComparison.Ordinal) + 3 - pad);
        var run = RunWithHostGcc("#include <stdint.h>\n#include <stdio.h>\n" + padFn + """
static int step;
static void ask(uintptr_t size, uint32_t flags) { printf("%d", es_muis_pad(&step, size, flags, 224)); }
int main(void)
{
    /* loading the library (as traced on hosted i386), then its init: the task structure and its stack; later requests are left alone */
    ask(4096, 1); ask(220, 0); ask(456, 0x10001); ask(220, 0); ask(224, 0x10001); ask(4096, 1); ask(4096, 1); ask(224, 0x10001); ask(4096, 1);
    printf(" %d\n", step);
    /* the task structure followed by something else, or too small: no padding */
    step = 0; ask(224, 0x10001); ask(4096, 0x10001); ask(4096, 1); ask(224, 0x10001); ask(36, 0); ask(4096, 1); ask(100, 0x10001); ask(4096, 1);
    printf(" %d\n", step);
    /* a larger task structure than our headers say (a newer system) */
    step = 0; ask(304, 0x10001); ask(4096, 1); ask(4096, 1);
    printf(" %d\n", step);
    return 0;
}
""");
        Assert.Equal(0, run.exitCode);
        Assert.Equal("000001000 2\n00000000 0\n010 2\n", run.output);

        // The entry in exec's vector is a stub in public memory that jumps through a slot: built here, in executable memory, it must
        // call the function the slot names, then the one it is retargeted to (the system's own AllocMem once the guard is done), and
        // keep working with nothing of the guard's function left (another task's patch may chain to it after the program exits).
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        int stubAt = c.IndexOf("static __attribute__((unused)) int es_muis_stub(", StringComparison.Ordinal);
        string stubFn = c.Substring(stubAt, c.IndexOf("\n}\n", stubAt, StringComparison.Ordinal) + 3 - stubAt);
        var stubRun = RunWithHostGcc("#include <stdint.h>\n#include <stdio.h>\n#include <sys/mman.h>\n" + stubFn + """
static long guarded(long a, long b) { return a * 100 + b; }
static long passthrough(long a, long b) { return a + b; }
int main(void)
{
    uint8_t *mem = mmap(0, 4096, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (mem == MAP_FAILED) { printf("nomap\n"); return 0; }
    if (!es_muis_stub(mem, (uintptr_t)guarded)) { printf("nostub\n"); return 1; }
    long (*entry)(long, long) = (long (*)(long, long))(void *)mem;
    printf("%ld ", entry(3, 4));
    *(volatile uintptr_t *)(mem + 8) = (uintptr_t)passthrough;
    printf("%ld\n", entry(3, 4));
    return 0;
}
""");
        Assert.Equal(0, stubRun.exitCode);
        Assert.True(stubRun.output == "304 7\n" || stubRun.output == "nomap\n", stubRun.output);
    }

    [Fact]
    public void A_program_class_can_derive_from_MuiArea_and_MuiCustomGroup_but_not_from_other_MUI_classes()
    {
        var ok = Compile(Program("class Dial : MuiArea { protected override void OnDraw(MuiRender r) { r.Fill(MuiPen.Fill, 0, 0, r.Width, r.Height); } }\n" +
            "class Box : MuiCustomGroup { protected override void OnAskMinMax(ref MuiMinMax size) { size.MinWidth = 10; } }",
            "var d = new Dial(); var b = new Box(); b.Add(d); Heap.Free(ref b);"), HostC);
        Assert.True(ok.Success, string.Join("\n", ok.Diagnostics));
        var bad = Compile(Program("class MyText : MuiText { public MyText() : base(\"x\") { } }", "var t = new MyText(); Heap.Free(ref t);"), HostC);
        Assert.False(bad.Success);
        Assert.Contains(bad.Diagnostics, d => d.Id == "ES0007");
    }

    /// <summary>
    /// The host string and object address tables of Cast are process-wide and samples run in parallel in one process (#141: "its text
    /// reads back" failed once). Without the lock a Dictionary mutated by two threads loses entries, throws, or hands two objects one address.
    /// </summary>
    [Fact]
    public unsafe void Cast_host_tables_survive_parallel_use()
    {
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        System.Threading.Tasks.Parallel.For(0, 16, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
        {
            try
            {
                for (int i = 0; i < 3000; i++)
                {
                    var s = $"cast-{t}-{i}";
                    var back = ESharp.Platform.Cast.PtrToString(ESharp.Platform.Cast.StringToPtr(s));
                    if (back != s) errors.Add($"{s} came back as {back}");
                }
            }
            catch (Exception e) { errors.Add(e.GetType().Name + ": " + e.Message); }
        });
        Assert.Empty(errors.Take(5));
    }
}
