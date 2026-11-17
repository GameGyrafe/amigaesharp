import ctypes,sys,time
x=ctypes.CDLL("libX11.so.6");t=ctypes.CDLL("libXtst.so.6")
x.XOpenDisplay.restype=ctypes.c_void_p
d=x.XOpenDisplay(sys.argv[1].encode());assert d
D=ctypes.c_void_p(d)
def fl(): x.XFlush(D)
for step in sys.argv[2].split(';'):
    p=step.split(None,1)
    if p[0]=='click':
        a,b=map(int,p[1].split(','))
        t.XTestFakeMotionEvent(D,-1,a-3,b-3,0);fl();time.sleep(.3)
        t.XTestFakeMotionEvent(D,-1,a,b,0);fl();time.sleep(.5)
        t.XTestFakeButtonEvent(D,1,1,0);fl();time.sleep(.2)
        t.XTestFakeButtonEvent(D,1,0,0);fl();time.sleep(.8)
    elif p[0]=='dbl':
        # double-click: two clicks well inside the Workbench/Wanderer double-click time
        a,b=map(int,p[1].split(','))
        t.XTestFakeMotionEvent(D,-1,a-3,b-3,0);fl();time.sleep(.3)
        t.XTestFakeMotionEvent(D,-1,a,b,0);fl();time.sleep(.5)
        for _ in range(2):
            t.XTestFakeButtonEvent(D,1,1,0);fl();time.sleep(.06)
            t.XTestFakeButtonEvent(D,1,0,0);fl();time.sleep(.12)
        time.sleep(.8)
    elif p[0]=='sleep':
        time.sleep(float(p[1]))
    elif p[0]=='key':
        c=int(p[1]);t.XTestFakeKeyEvent(D,c,1,0);fl();time.sleep(.2);t.XTestFakeKeyEvent(D,c,0,0);fl();time.sleep(.8)
x.XCloseDisplay(D)
