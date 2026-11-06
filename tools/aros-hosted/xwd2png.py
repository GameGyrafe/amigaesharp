import sys,struct,zlib
d=open(sys.argv[1],'rb').read()
hs=struct.unpack('>25I',d[:100])
hdr,ver,fmt,depth,w,h,xoff,bo,bu,bbo,bpad,bpp,bpl,vc,rm,gm,bm,bpr,cs,ncol=hs[:20]
off=hdr+ncol*12
px=d[off:]
rows=[]
for y in range(h):
    r=px[y*bpl:y*bpl+w*(bpp//8)]
    out=bytearray()
    for x in range(w):
        v=int.from_bytes(r[x*(bpp//8):(x+1)*(bpp//8)],'little' if bo==0 else 'big')
        out+=bytes(((v&rm)>>(rm&-rm).bit_length()-1,(v&gm)>>(gm&-gm).bit_length()-1,(v&bm)>>(bm&-bm).bit_length()-1))
    rows.append(b'\0'+bytes(out))
def ch(t,b):c=struct.pack('>I',len(b))+t+b;return c+struct.pack('>I',zlib.crc32(t+b))
open(sys.argv[2],'wb').write(b'\x89PNG\r\n\x1a\n'+ch(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))+ch(b'IDAT',zlib.compress(b''.join(rows)))+ch(b'IEND',b''))
