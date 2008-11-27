#include <windows.h>
#include <imagehlp.h>
#include <tlhelp32.h>
#include <vcl.h>
#pragma hdrstop
#include "callstack.h"
//---------------------------------------------------------------------------
#pragma package(smart_init)


//=============================================================================
// callstack.cpp -- to get a readout of the current call-stack.
//   (involves getting an enumeration of all current processes, either
//   with toolhelp32 or psapi; loading symbol information for each;
//   loading imagehlp; using it to get a stack-trace.
// All the dlls are loaded dynamically, the first time they're needed.
//   Thus, if you never call dcallstack() in your application, there won't
//   be any slowdown.
// There's lots of OutputDebugString that happen. That shouldn't be a problem!
//   After all, if your problem is serious enough for you to need a callstack,
//   then you want all the diagnostics you can have!
//
// IMPORTANT -- to get the callstack it uses standard call-stack routines
//   that are built into windows. These require MS-format debug information,
//   which can be found in .DBG files. Borland does not generate MS-compatible
//   debug files. So, you need to generate a .DBG file yourself. Read the
//   readme.txt for an explanation of how to get them, and a discussion.
//=============================================================================




typedef BOOL (__stdcall *SYMCLEANUPPROC)( IN HANDLE hProcess );
typedef PVOID (__stdcall *SYMFUNCTIONTABLEACCESSPROC)( HANDLE hProcess, DWORD AddrBase );
typedef BOOL (__stdcall *SYMGETLINEFROMADDRPROC)( IN HANDLE hProcess, IN DWORD dwAddr,	OUT PDWORD pdwDisplacement, OUT PIMAGEHLP_LINE Line );
typedef DWORD (__stdcall *SYMGETMODULEBASEPROC)( IN HANDLE hProcess, IN DWORD dwAddr );
typedef BOOL (__stdcall *SYMGETMODULEINFOPROC)( IN HANDLE hProcess, IN DWORD dwAddr, OUT PIMAGEHLP_MODULE ModuleInfo );
typedef DWORD (__stdcall *SYMGETOPTIONSPROC)( VOID );
typedef BOOL (__stdcall *SYMGETSYMFROMADDRPROC)(IN HANDLE hProcess, IN DWORD dwAddr,	OUT PDWORD pdwDisplacement, OUT PIMAGEHLP_SYMBOL Symbol );
typedef BOOL (__stdcall *SYMINITIALIZEPROC)( IN HANDLE hProcess, IN PSTR UserSearchPath, IN BOOL fInvadeProcess );
typedef DWORD (__stdcall *SYMLOADMODULEPROC)(IN HANDLE hProcess, IN HANDLE hFile,IN PSTR ImageName, IN PSTR ModuleName, IN DWORD BaseOfDll, IN DWORD SizeOfDll );
typedef DWORD (__stdcall *SYMSETOPTIONSPROC)(IN DWORD SymOptions);
typedef BOOL (__stdcall *STACKWALKPROC)(DWORD MachineType, HANDLE hProcess,HANDLE hThread, LPSTACKFRAME StackFrame, PVOID ContextRecord,	PREAD_PROCESS_MEMORY_ROUTINE ReadMemoryRoutine,	PFUNCTION_TABLE_ACCESS_ROUTINE FunctionTableAccessRoutine,	PGET_MODULE_BASE_ROUTINE GetModuleBaseRoutine,	PTRANSLATE_ADDRESS_ROUTINE TranslateAddress );
typedef DWORD (__stdcall WINAPI *UNDECORATESYMBOLNAMEPROC)(PCSTR DecoratedName, PSTR UnDecoratedName,DWORD UndecoratedLength, DWORD Flags );
typedef BOOL (__stdcall *SYMENUMERATESYMBOLSPROC)(IN HANDLE hProcess, IN DWORD base,IN PVOID proc,IN PVOID dat);
//
typedef HANDLE (__stdcall *CREATETOOLHELP32SNAPSHOTPROC)( DWORD dwFlags, DWORD th32ProcessID );
typedef BOOL (__stdcall *MODULE32FIRSTPROC)( HANDLE hSnapshot, LPMODULEENTRY32 lpme );
typedef BOOL (__stdcall *MODULE32NEXTPROC)( HANDLE hSnapshot, LPMODULEENTRY32 lpme );
//
typedef struct _MODULEINFO {LPVOID lpBaseOfDll; DWORD SizeOfImage; LPVOID EntryPoint;} MODULEINFO, *LPMODULEINFO;
typedef BOOL (__stdcall *ENUMPROCESSMODULESPROC)( HANDLE hProcess, HMODULE *lphModule, DWORD cb, LPDWORD lpcbNeeded );
typedef DWORD (__stdcall *GETMODULEFILENAMEEXPROC)( HANDLE hProcess, HMODULE hModule, LPSTR lpFilename, DWORD nSize );
typedef DWORD (__stdcall *GETMODULEBASENAMEPROC)( HANDLE hProcess, HMODULE hModule, LPSTR lpFilename, DWORD nSize );
typedef BOOL (__stdcall *GETMODULEINFORMATIONPROC)( HANDLE hProcess, HMODULE hModule, LPMODULEINFO pmi, DWORD nSize );


SYMCLEANUPPROC             pSymCleanup = NULL;
SYMFUNCTIONTABLEACCESSPROC pSymFunctionTableAccess = NULL;
SYMGETLINEFROMADDRPROC     pSymGetLineFromAddr = NULL;
SYMGETMODULEBASEPROC       pSymGetModuleBase = NULL;
SYMGETMODULEINFOPROC       pSymGetModuleInfo = NULL;
SYMGETOPTIONSPROC          pSymGetOptions = NULL;
SYMGETSYMFROMADDRPROC      pSymGetSymFromAddr = NULL;
SYMINITIALIZEPROC          pSymInitialize = NULL;
SYMLOADMODULEPROC          pSymLoadModule = NULL;
SYMSETOPTIONSPROC          pSymSetOptions = NULL;
STACKWALKPROC              pStackWalk = NULL;
UNDECORATESYMBOLNAMEPROC   pUnDecorateSymbolName = NULL;
SYMENUMERATESYMBOLSPROC    pSymEnumerateSymbols = NULL;
HINSTANCE hImagehlpDll=NULL;
//
CREATETOOLHELP32SNAPSHOTPROC pCreateToolhelp32Snapshot = NULL;
MODULE32FIRSTPROC pModule32First = NULL;
MODULE32NEXTPROC pModule32Next = NULL;
HINSTANCE hToolHelp = NULL;
//
ENUMPROCESSMODULESPROC pEnumProcessModules = NULL;
GETMODULEFILENAMEEXPROC pGetModuleFileNameEx = NULL;
GETMODULEBASENAMEPROC pGetModuleBaseName = NULL;
GETMODULEINFORMATIONPROC pGetModuleInformation = NULL;
HINSTANCE hPsapi = NULL;
//
bool isinited=false;// have we tried at least once to init?
bool issucc=false;  // was it succesfull?
bool usemods=false; // will we also be able to enumerate modules?
bool usesyms=false; // will we also be able to use symbols?
int modcount=0; // the last time we loaded symbols, how many modules was it for?






//=============================================================================
// Just some debugging functions, to write a log to OutputDebugString
//=============================================================================
//
AnsiString __fastcall le()
{ LPVOID lpMsgBuf;
  FormatMessage(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,NULL,GetLastError(),MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),(LPTSTR) &lpMsgBuf,0,NULL);
  AnsiString s=AnsiString((char*)lpMsgBuf);
  LocalFree( lpMsgBuf );
  while (s[s.Length()]=='\r' || s[s.Length()]=='\n') s=s.SubString(1,s.Length()-1);
  return s;
}
void __fastcall db(AnsiString s) {OutputDebugString((s+"\n").c_str());}
void __fastcall dble(AnsiString s) {db(s+" "+le());}










//=============================================================================
// FillModuleList -- gets a list of all modules (i.e. dlls, current exe, packages ...)
//   There are two implementations. First, we try to get it using toolhelp.
//   If that fails, then we try using PSAPI. We load both toolhelp and PSAPI
//   dynamically.
// EnsureModuleSymbolsLoaded - we use 'modcount' as a sort of checksum, recording
//   how many modules' symbols have been loaded. Initially zero. Every time
//   someone calls this function we check how many modules are currently loaded.
//   If there are != modcount, then someone's been loading or unloading dlls,
//   so we take the opportunity to FillModuleList and load symbols from every
//   single one of them again. Obviously the checksum might break, if you
//   unload one library and load another. Be careful!
//=============================================================================
//
typedef struct {AnsiString imageName; AnsiString moduleName; DWORD baseAddress; DWORD size;} TModuleEntry;
//
void __fastcall FillModuleListTH32(TList *modules, DWORD pid )
{ HANDLE hSnap=pCreateToolhelp32Snapshot(TH32CS_SNAPMODULE,pid);
  if (hSnap==(HANDLE)-1) return;
  MODULEENTRY32 me; ZeroMemory(&me,sizeof(me)); me.dwSize=sizeof(me);
  bool keepgoing = !!pModule32First(hSnap,&me);
  while (keepgoing)
  { TModuleEntry *e=new TModuleEntry;
    e->imageName=me.szExePath;
    e->moduleName=me.szModule;
    e->baseAddress=(DWORD)me.modBaseAddr;
    e->size=me.modBaseSize;
    db("snapshot modbase=0x"+IntToHex((int)e->baseAddress,8)+" modbasesize=0x"+IntToHex((int)e->size,8)+" module="+e->moduleName+" exepath="+e->imageName);
    modules->Add(e);
    keepgoing = !!pModule32Next(hSnap,&me);
  }
  CloseHandle(hSnap);
}

void __fastcall FillModuleListPSAPI(TList *modules, DWORD pid, HANDLE hProcess )
{ HMODULE hMods[MAX_PATH];
  BOOL bres; DWORD size;
  bres=pEnumProcessModules(hProcess,hMods,MAX_PATH,&size); if (!bres) return;
  if (size>MAX_PATH) {db("EnumProcessModules failed: not enough space.");return;}
  int nummods = size/sizeof(HMODULE);
  for (int i=0; i<nummods; i++)
  { MODULEINFO mi; TModuleEntry *e=new TModuleEntry;
    pGetModuleInformation(hProcess,hMods[i],&mi,sizeof(mi));
    e->baseAddress=(DWORD)mi.lpBaseOfDll;
    e->size=mi.SizeOfImage;
    char buf[MAX_PATH]; buf[0]='\0'; pGetModuleFileNameEx(hProcess,hMods[i],buf,MAX_PATH);
    e->imageName=buf;
    buf[0]='\0'; pGetModuleBaseName(hProcess,hMods[i],buf,MAX_PATH);
    e->moduleName=buf;
    db("snapshot modbase=0x"+IntToHex((int)e->baseAddress,8)+" modbasesize=0x"+IntToHex((int)e->size,8)+" module="+e->moduleName+" exepath="+e->imageName);
    modules->Add(e);
  }
}

void __fastcall EnsureModuleSymbolsLoaded()
{ if (!usemods) return;
  HANDLE hProcess=GetCurrentProcess(); DWORD pid=GetCurrentProcessId();
  TList *modules=new TList();
  if (hToolHelp!=NULL) FillModuleListTH32(modules,pid);
  else if (hPsapi!=NULL) FillModuleListPSAPI(modules,pid,hProcess);
  // only reload symbols if our naive checksum has indicated a change
  if (modules->Count!=modcount)
  { for (int i=0; i<modules->Count; i++)
    { TModuleEntry *mod=(TModuleEntry*)modules->Items[i];
      DWORD time=GetTickCount();
      BOOL bres=pSymLoadModule(hProcess,0,mod->imageName.c_str(),mod->moduleName.c_str(),mod->baseAddress,mod->size);
      int dt=(int)(GetTickCount()-time);
      if (bres) db("Symbols loaded: "+mod->moduleName+" ["+dt+"ms]");
      else dble("Failed to load symbols for "+mod->moduleName+" ["+dt+"ms]");
      delete mod;
    }
  }
  delete modules;
}








//=============================================================================
// dinit - This function gets called automatically the first time you try
//   to get the callstack. It loads the imagehlp library dynamically,
//   sets up the symbol manager, loads in symbols for all running processes.
// dexit - this function gets called automatically. It cleanups the
//   symbol manager and unloads the dll
//=============================================================================
//
void __fastcall dinit()
{ if (isinited) return; isinited=true; issucc=false; usemods=false; usesyms=false;
  db("Initing...");
  hImagehlpDll=LoadLibrary("imagehlp.dll"); if (hImagehlpDll==NULL) {db("Failed to load library.");return;}
  pSymCleanup = (SYMCLEANUPPROC) GetProcAddress( hImagehlpDll, "SymCleanup" );
  pSymFunctionTableAccess = (SYMFUNCTIONTABLEACCESSPROC) GetProcAddress( hImagehlpDll, "SymFunctionTableAccess" );
  pSymGetLineFromAddr = (SYMGETLINEFROMADDRPROC) GetProcAddress( hImagehlpDll, "SymGetLineFromAddr" );
  pSymGetModuleBase = (SYMGETMODULEBASEPROC) GetProcAddress( hImagehlpDll, "SymGetModuleBase" );
  pSymGetModuleInfo = (SYMGETMODULEINFOPROC) GetProcAddress( hImagehlpDll, "SymGetModuleInfo" );
  pSymGetOptions = (SYMGETOPTIONSPROC) GetProcAddress( hImagehlpDll, "SymGetOptions" );
  pSymGetSymFromAddr = (SYMGETSYMFROMADDRPROC) GetProcAddress( hImagehlpDll, "SymGetSymFromAddr" );
  pSymInitialize = (SYMINITIALIZEPROC) GetProcAddress( hImagehlpDll, "SymInitialize" );
  pSymSetOptions = (SYMSETOPTIONSPROC) GetProcAddress( hImagehlpDll, "SymSetOptions" );
  pStackWalk = (STACKWALKPROC) GetProcAddress( hImagehlpDll, "StackWalk" );
  pUnDecorateSymbolName = (UNDECORATESYMBOLNAMEPROC) GetProcAddress( hImagehlpDll, "UnDecorateSymbolName" );
  pSymLoadModule = (SYMLOADMODULEPROC) GetProcAddress( hImagehlpDll, "SymLoadModule" );
  pSymEnumerateSymbols = (SYMENUMERATESYMBOLSPROC) GetProcAddress( hImagehlpDll, "SymEnumerateSymbols");
  bool ok=true;
  if (pSymCleanup==NULL) ok=false;
  if (pSymFunctionTableAccess==NULL) ok=false;
  //if (pSymGetLineFromAddr==NULL) ok=false; // This function isn't essential.
  if (pSymGetModuleBase==NULL) ok=false;
  if (pSymGetModuleInfo==NULL) ok=false;
  if (pSymGetOptions==NULL) ok=false;
  if (pSymGetSymFromAddr==NULL) ok=false;
  if (pSymInitialize==NULL) ok=false;
  if (pSymSetOptions==NULL) ok=false;
  if (pStackWalk==NULL) ok=false;
  if (pUnDecorateSymbolName==NULL) ok=false;
  if (pSymLoadModule==NULL) ok=false;
  if (pSymEnumerateSymbols==NULL) ok=false;
  if (!ok) {FreeLibrary(hImagehlpDll); db("Failed to find all imagehlp functions.");return;}
  //
  // Initialise one of our module-walking friends
  // I think the toolhelp DLL is called tlhelp32.dll on Win9X, so we try both
  db("Trying to load toolhelp...");
  const char *dllname[] = { "kernel32.dll", "tlhelp32.dll" };
  for (int i=0; i<2; i++)
  { hToolHelp=LoadLibrary(dllname[i]);
    if (hToolHelp==NULL) continue;
    pCreateToolhelp32Snapshot = (CREATETOOLHELP32SNAPSHOTPROC)GetProcAddress(hToolHelp,"CreateToolhelp32Snapshot");
    pModule32First = (MODULE32FIRSTPROC)GetProcAddress(hToolHelp,"Module32First");
    pModule32Next = (MODULE32NEXTPROC)GetProcAddress(hToolHelp,"Module32Next");
    if (pCreateToolhelp32Snapshot!=0 && pModule32First!=0 && pModule32Next!=0) break;
    FreeLibrary(hToolHelp); hToolHelp=NULL;
  }
  if (hToolHelp==NULL)
  { db("Trying to load PSAPI...");
    hPsapi = LoadLibrary("psapi.dll");
    pEnumProcessModules = (ENUMPROCESSMODULESPROC)GetProcAddress(hPsapi,"EnumProcessModules");
    pGetModuleFileNameEx = (GETMODULEFILENAMEEXPROC)GetProcAddress(hPsapi,"GetModuleFileNameExA");
    pGetModuleBaseName = (GETMODULEBASENAMEPROC)GetProcAddress(hPsapi,"GetModuleBaseNameA");
    pGetModuleInformation = (GETMODULEINFORMATIONPROC)GetProcAddress(hPsapi,"GetModuleInformation");
    if (pEnumProcessModules==0 || pGetModuleFileNameEx==0 || pGetModuleBaseName==0 || pGetModuleInformation==0)
    { FreeLibrary(hPsapi); hPsapi=NULL;
    }
  }
  usemods = (hToolHelp!=NULL || hPsapi!=NULL);
  if (!usemods)
  { db("Could not load either toolhelp or psapi. There will be no symbol information.");
  }
  //
  // build symbol search: name of apps directory, without trailing slash, plus env.vars
  if (usemods)
  { AnsiString sympath=ExtractFilePath(Application->ExeName);
    if (sympath[sympath.Length()]=='\\') sympath=sympath.SubString(1,sympath.Length()-1);
    char buf[MAX_PATH*4]; DWORD dres;
    dres=GetEnvironmentVariable("_NT_SYMBOL_PATH", buf,MAX_PATH); if (dres>0) sympath=sympath+";"+AnsiString(buf);
    dres=GetEnvironmentVariable("_NT_ALTERNATE_SYMBOL_PATH",buf,MAX_PATH); if (dres>0) sympath=sympath+";"+AnsiString(buf);
    dres=GetEnvironmentVariable("SYSTEMROOT",buf,MAX_PATH); if (dres>0) sympath=sympath+";"+AnsiString(buf);
    db("sympath: "+sympath);
    usesyms = pSymInitialize(GetCurrentProcess(),sympath.c_str(),false);
    if (usesyms)
    { DWORD symOptions = pSymGetOptions();
	  symOptions |= SYMOPT_LOAD_LINES;
	  //symOptions &= ~SYMOPT_UNDNAME;
	  symOptions |= SYMOPT_CASE_INSENSITIVE;
	  symOptions |= SYMOPT_FAIL_CRITICAL_ERRORS;
	  symOptions |= SYMOPT_UNDNAME;
	  symOptions |= SYMOPT_DEFERRED_LOADS;
	  pSymSetOptions(symOptions);
    }
    else
    { FreeLibrary(hImagehlpDll); hImagehlpDll=NULL;
      if (hToolHelp!=NULL) FreeLibrary(hToolHelp); hToolHelp=NULL;
      if (hPsapi!=NULL) FreeLibrary(hPsapi); hPsapi=NULL;
      db("Syms failed to initialize. There will be no symbol information");
    }
  }
  //
  //
  issucc=true;
}

void __fastcall dexit()
{ if (!isinited) return;
  db("Terminating...");
  if (hImagehlpDll!=NULL)
  { pSymCleanup(GetCurrentProcess());
    if (hToolHelp!=NULL) FreeLibrary(hToolHelp); hToolHelp=NULL;
    if (hPsapi!=NULL) FreeLibrary(hPsapi); hPsapi=NULL;
    FreeLibrary(hImagehlpDll); hImagehlpDll=NULL;
  }
  isinited=false;
}
class TDummyDExit {public: ~TDummyDExit() {dexit();} } DummyDExit;
// That DummyDExit is just a corny way of making sure that the exit routine
// gets called



// The following is defined for x86 (XP and higher), x64 and IA64:
#define GET_CURRENT_CONTEXT(c, contextFlags) \
  do { \
	memset(&c, 0, sizeof(CONTEXT)); \
	c.ContextFlags = contextFlags; \
	RtlCaptureContext(&c); \
} while(0);


//=============================================================================
// dcallstack - the only useful function in this entire module! first, if
//   it hasn't been done already, it ensures that dinit has been called.
//   Retrieves the current context. Then uses StackWalk(...) to retrieve
//   the stack frame by frame.
//=============================================================================
const int maxframes=0; // unbounded number of frames
//
AnsiString __fastcall dcallstack()
{ dinit(); if (!issucc) return "<failed to init debugging>";
  HANDLE hProcess=GetCurrentProcess(),
  hThread=GetCurrentThread();

  CONTEXT ctx;
  BOOL bres;

  if (hThread == GetCurrentThread())
  {
	//use this one, otherwise fails on WinXp!
	GET_CURRENT_CONTEXT(ctx, CONTEXT_ALL);
  }
  else
  {
	ZeroMemory(&ctx,sizeof(ctx));
	ctx.ContextFlags=CONTEXT_ALL;
	bres = GetThreadContext(hThread,&ctx);
	if (!bres) return "<failed to get context>";
  };

  EnsureModuleSymbolsLoaded();

  AnsiString callstack="";
  //
  STACKFRAME s; ZeroMemory(&s,sizeof(s));
  s.AddrPC.Offset    = ctx.Eip;
  s.AddrPC.Mode      = AddrModeFlat;
  s.AddrFrame.Offset = ctx.Ebp;
  s.AddrFrame.Mode   = AddrModeFlat;
  //also this must be filled?
  s.AddrStack.Offset = ctx.Esp;
  s.AddrStack.Mode   = AddrModeFlat;

  IMAGEHLP_SYMBOL *pSym = (IMAGEHLP_SYMBOL *)malloc(sizeof(IMAGEHLP_SYMBOL) + MAX_PATH );
  ZeroMemory(pSym,sizeof(IMAGEHLP_SYMBOL)+MAX_PATH); pSym->SizeOfStruct=sizeof(IMAGEHLP_SYMBOL); pSym->MaxNameLength=MAX_PATH;
  IMAGEHLP_LINE Line; ZeroMemory(&Line,sizeof(Line)); Line.SizeOfStruct=sizeof(Line);
  IMAGEHLP_MODULE Module; ZeroMemory(&Module,sizeof(Module)); Module.SizeOfStruct=sizeof(Module);
  //
  for (int nframe=0; maxframes<=0 || nframe<maxframes; nframe++)
  { AnsiString desc="";
	bres=pStackWalk(IMAGE_FILE_MACHINE_I386,hProcess,hThread,&s,&ctx,NULL,pSymFunctionTableAccess,pSymGetModuleBase,NULL);
	if (!bres) break;
	db(AnsiString(nframe)+": "+AnsiString(s.Far?"F":" ")+AnsiString(s.Virtual?"V":" ")+" pc=0x"+IntToHex((int)s.AddrPC.Offset,8)+" ret=0x"+IntToHex((int)s.AddrReturn.Offset,8)+" frame=0x"+IntToHex((int)s.AddrFrame.Offset,8)+" stack=0x"+IntToHex((int)s.AddrStack.Offset,8));
    if (s.AddrPC.Offset!=0) desc=desc+IntToHex((int)s.AddrPC.Offset,8)+" ";
	else db("<nosymbols for PC=0>");
    if (s.AddrPC.Offset!=0 && usesyms)
    { // First, the symbol
      ZeroMemory(pSym,sizeof(IMAGEHLP_SYMBOL)+MAX_PATH); pSym->SizeOfStruct=sizeof(IMAGEHLP_SYMBOL); pSym->MaxNameLength=MAX_PATH;
      AnsiString cursym="";
      DWORD offsetFromSymbol=0;
      bres=pSymGetSymFromAddr(hProcess, s.AddrPC.Offset,&offsetFromSymbol,pSym);
      if (!bres)
      { if (GetLastError()!=487) dble("SymGetSymFromAddr: failed.");
      }
      else
	  { char undName[MAX_PATH], undFullName[MAX_PATH];
        pUnDecorateSymbolName(pSym->Name,undName,MAX_PATH,UNDNAME_NAME_ONLY);
        pUnDecorateSymbolName(pSym->Name,undFullName,MAX_PATH,UNDNAME_COMPLETE);
        db(undName + (offsetFromSymbol==0? (AnsiString("")):(" + "+IntToHex((int)offsetFromSymbol,4))));
        db("sig="+AnsiString(pSym->Name)+" decl="+undFullName);
        desc=desc+AnsiString(undFullName); cursym=AnsiString(undFullName);
        if (offsetFromSymbol!=0) desc=desc+"+0x"+IntToHex((int)offsetFromSymbol,8);
      }
      // Third, the line number.
      if (pSymGetLineFromAddr!=NULL) // only present in NT5
      { bres=pSymGetLineFromAddr(hProcess,s.AddrPC.Offset,&offsetFromSymbol,&Line);
        if (!bres) {if (GetLastError()!=487) dble("SymGetLineFromAddr failed.");}
		else
        { db("line="+AnsiString(Line.FileName)+"("+Line.LineNumber+") + "+offsetFromSymbol);
          desc=desc+" ("+AnsiString(Line.FileName)+" "+Line.LineNumber+")";
        }
      }
      // Fourth, the module info
      bres=SymGetModuleInfo(hProcess,s.AddrPC.Offset,&Module);
      if (!bres) dble("SymGetModuleInfo failed.");
      else
      { AnsiString type;
        switch (Module.SymType)
        { case SymNone: type="-nosymbols-"; break;
          case SymCoff: type="COFF"; break;
          case SymCv: type="CV"; break;
          case SymPdb: type="PDB"; break;
          case SymExport: type="-exported-"; break;
          case SymDeferred: type="-deferred-"; break;
		  case SymSym: type="SYM"; break;
          default: type="type="+AnsiString(Module.SymType);
        }
        db("Mod: "+AnsiString(Module.ModuleName)+"["+Module.ImageName+"] base="+IntToHex((int)Module.BaseOfImage,8));
        db("Sym: "+type+" file="+Module.LoadedImageName);
        desc=desc+" ["+AnsiString(Module.ModuleName)+"]";
      }
    } // end if (usesyms)
	//if (nframe!=0)
	callstack=callstack+desc+"\r\n";
	// we don't print the first one, since it was just inside GetContextThread
    if (s.AddrReturn.Offset==0) {SetLastError(0); break;}
  }
  //
  if (GetLastError()!=0) dble("StackWalk: had some error or other.");
  free(pSym);
  return callstack;
}


