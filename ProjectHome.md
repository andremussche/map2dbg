Map2Dbg is a mall tool to convert a .map file to a .dbg file.

A map file is a Borland/Codegear debug file, created by Delphi
and C++Builder. However, it is not compatible with Microsoft's debug file format (.dbg), so it cannot be used with Microsoft debug tools (windbg.exe for example).

Map2dbg is originally created by Lucian Wischik:
http://www.wischik.com/lu/programmer/

A small fix is made to his version to make it compatible with newer .map files (Delphi and C++Builder 2006 - 2009).
It is also compatible with the free Turbo Delphi and Turbo C++Builder:
http://turboexplorer.com/downloads

Goal of this open source project (with approval of Lucian Wischik) is:
  1. Keep compatible with every new version of Delphi/C++Builder
  1. Convert more debug info (e.g. line numbering, source file names)
  1. Port it to Delphi (so it can be included in Delphi projects)
  1. Make a plugin for Delphi/CBuilder, so converting is done after each build

Because of my little knowledge and experience of C++: **if you can help** me with point 2 (line numbering + source files), that would be very nice!


---

**Update:**
[20-03-2012: tds2pdb converter](http://map2dbg.googlecode.com/files/tds2pdb.zip)


---

**Note:**

You can also use [tds2dbg.exe](http://sourceforge.net/projects/tds2dbg/files/) to convert a "TD32 Debug Info" file (.tds) to a .dbg file.<br />
For Delphi: use [tdstrp32.exe](http://map2dbg.googlecode.com/files/tdstrp32.zip) to extract TD32 from a Delphi exe to a .tds file (use tdstrp32.exe -s <app.exe>).<br />
Read [this page](http://www.automatedqa.com/support/viewarticle/?aid=14469) for which compiler options to set to get TDS/TD32 debug info.

There is also an project named ["tds2pdb"](http://sourceforge.net/projects/tds2pdb/) which converts and C++ .tds file to a PDB file.
I made some small modifications so it can convert a Delphi .tds (TD32) file to .pdb file:
http://map2dbg.googlecode.com/files/tds2pdb.zip


---

**MiniDump reader:** (.dmp files)

I made a MiniDumpReader in Delphi myself, so it has full support for all Delphi debug symbols, including line number support:
https://asmprofiler.googlecode.com/svn/trunk/MiniDumpReader/ViewMinidump.exe

Tip: you can drag and drop files on it too