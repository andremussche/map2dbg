Many thanks to the creator of the original project:
http://tds2pdb.sourceforge.net/

I only commented some code, so it tries to continue to 
generate .pdb files for Delphi executables too.
(original project only supported C++Builder)

Steps how to use it with Delphi (32 and 64bit!):
- Compile your project with "TD32" (D7?) or with "Debug Information" (Compiler -> Linker options, D2010, XE2)
- Strip the TD32 information into a seperate .tds file with:
  tdstrp32.exe -s <yourproject.exe>
  Note: don't use the -s switch with a 64bit exe -> it will corrupt your exe! (the .tds is OK)
- Convert the .tds file into a .pdb file with:  
  tds2pdb.exe <yourproject.tds>
- Run your program
- Attach a debugger or a task manager, and write the path to the .pdb file:
  - WinDbg.exe (Microsoft Debugging Tools for Windows, x86 for 32bit, x64 for 64bit) 
    - File -> Symbol file path
  - Proces Explorer
    - Options -> Configure symbols
  - Proces Hacker
    - Hacker -> Options -> Symbols
- View the stack of your thread -> you should see "your" class and functions names now!