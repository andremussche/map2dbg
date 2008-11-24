#include <windows.h>
#include <imagehlp.h>
#include <stdio.h>
#include <system.hpp>
#include <sysutils.hpp>
#include <classes.hpp>
#include "cvexefmt.h"
#pragma hdrstop
#include "convert.h"
//---------------------------------------------------------------------------
#pragma package(smart_init)

//============================================================================
// Convert -- converts Borland's MAP file format, into Microsoft's DBG format,
// and marks the executable as 'debug-stripped'. See readme.txt for a discussion.
// This code is (c) 2000-2002 Lucian Wischik.
//============================================================================

//============================================================================
// First, just a small debugging function and some code for dynamic-loading dll
// and an init and automatic-exit routine
//============================================================================
AnsiString __fastcall le()
{ LPVOID lpMsgBuf;
  FormatMessage(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,NULL,GetLastError(),MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),(LPTSTR) &lpMsgBuf,0,NULL);
  AnsiString s=AnsiString((char*)lpMsgBuf);
  LocalFree( lpMsgBuf );
  while (s[s.Length()]=='\r' || s[s.Length()]=='\n') s=s.SubString(1,s.Length()-1);
  return s;
}
//
typedef BOOL (__stdcall *MAPANDLOADPROC)(IN LPSTR ImageName, IN LPSTR DllPath, OUT PLOADED_IMAGE LoadedImage, IN BOOL DotDll, IN BOOL ReadOnly);
typedef BOOL (__stdcall *UNMAPANDLOADPROC)(IN PLOADED_IMAGE LoadedImage);
MAPANDLOADPROC pMapAndLoad = NULL;
UNMAPANDLOADPROC pUnMapAndLoad = NULL;
bool isinit=false, issucc=false; HINSTANCE himagehlp=NULL;
//
void iinit()
{
  if (isinit)
	return;
  isinit=true;
  issucc=false;
  himagehlp = LoadLibrary("imagehlp.dll");
  if (himagehlp==0)
  {throw new Exception("The system DLL imagehlp.dll was not found.");}
  pMapAndLoad   = (MAPANDLOADPROC)GetProcAddress(himagehlp,"MapAndLoad");
  pUnMapAndLoad = (UNMAPANDLOADPROC)GetProcAddress(himagehlp,"UnMapAndLoad");
  if (pMapAndLoad==0 || pUnMapAndLoad==0)
  {
	FreeLibrary(himagehlp);
	himagehlp=NULL;
	throw new Exception("The system DLL imagehlp.dll did not have the required functionality.");
  }
  issucc=true;
}
void iexit()
{
  if (himagehlp!=NULL)
	FreeLibrary(himagehlp);
  himagehlp=NULL;
  isinit=false;
}

class TAutoExitClass
{
  public: ~TAutoExitClass()
  {
	iexit();
  }
} DummyAutoExit;

//============================================================================
// TDebugFile -- for creating a .DBG file from scratch
// methods TDebugFile(fnexe,fndbg), AddSymbol(seg,off,name), End()
// They return a 'bool' for success or failure. The text string 'err'
// reports what that error was.
// End is automatically called by the destructor. But you might want
// to call it yourself, beforehand, for manual error checking.
// If file is non-null then it means we have succesfully set things up.
//============================================================================
// File format is as follows:
// In each column the offsets are relative to the start of that column.
// Thus, oCv is relative to the file as a whole; cvoSstModule is relative to the
// start of the SstModule; gpoSym is relative to the start of GlobalPub module.
//
// @0. IMAGE_SEPARATE_DEBUG_HEADER -- header of the file. [WriteDBGHeader]
// @.  numsecs * IMAGE_SECTION_HEADER -- executable's section table. [WriteSectionTable]
// @.  1 * IMAGE_DEBUG_DIRECTORY -- only one cv-type debug directory. [WriteDbgDirectory]
// @oCv. <cv-data> -- this is the raw data. of size szCv
//   @0. OMFSignature -- 'NB09'+omfdir. [in WriteCv]
//   @8. OMFDirHeader -- subsection directory header. [in WriteCv]
//   @.  3 * OMFDirEntry -- 3 directory entries: sstModule, sstGlobalPub, sstSegMap. [in WriteCv]
//   @cvoSstModule. <sst-module>, of length SstModuleSize. [WriteSstModule]
//     @0. OMFModule
//     @.  numsecs * OMFSegDest
//     @.  modname, of size fnsize.
//   @cvoGlobalPub. <global-pub>, of length GlobalPubSize.
//     @0. OMFSymHash -- [WriteGlobalPubHeader]
//     @.  nSymbols * var. Variable-sized sympols. [WriteSymbol]
//     @gpoSym. always points to the next symbol to write, is relative to the start of global-pub
//   @cvoSegMap. <seg-map>, of length SetMapSize. [WriteSegMap]
//     @0. OMFSegMap
//     @.  nsec * OMFSegMapDesc
//
// Start
//   * numsec deduced from the executable-image.
//   * oCv is easy
//   * cvoSstModule, szSstModule are constant. szModName is easy.
//   * cvoGlobalPub just comes after, gpoSym initialized to after the OMFSymHash
//     [don't write anything yet]
// AddEntry
//   * increases gpoSym. [WriteSymbol]
// Finish
//   * cvoSegMap = cvoGlobalPub + gpoSym.
//     [WriteDBGHeader, WriteSectionTable, WriteDbgDirectory, WriteCv...]
//     [... WriteSstModule, WriteGlobalPubHeader, WriteSegMap]
//
class TDebugFile
{
public:
  TDebugFile(AnsiString afnexe,AnsiString afndbg) : file(NULL), ismapped(false), err(""), fnexe(afnexe), fndbg(afndbg) {}
  ~TDebugFile()
  {
	End();
	if (ismapped)
	  pUnMapAndLoad(&image);
	ismapped=false;
	if (file!=NULL)
	  fclose(file);
	file=NULL;
  }
  bool AddSymbol(unsigned short seg, unsigned long offset, AnsiString symbol);
  bool End(); // to flush the thing to disk.
  AnsiString err;
protected:
  AnsiString fnexe, fndbg; // keep a copy of the arguments to the constructor. We don't init until later.
  AnsiString modname;
  unsigned int szModName;
  LOADED_IMAGE image;
  bool ismapped; // we load the input exe into this image
  FILE *file; // the output file
  unsigned long oCv;          // offset to 'cv' data, relative to the start of the output file
  unsigned long cvoSstModule; // offset to sstModule within cv block
  unsigned long szSstModule;  // size of that sstModule
  unsigned long cvoGlobalPub; // offset to GlobalPub within cv block
  unsigned long gpoSym;       // offset to next-symbol-to-write within GlobalPub block
  bool check(unsigned long pos,AnsiString s)
  {
	if (pos!=(unsigned long)ftell(file) && err!="")
	{
	  err=s;
	  return false;
	}
	else
	  return true;
  }
  bool EnsureStarted(); // this routine is called automatically by AddSymbol and End
};


bool TDebugFile::EnsureStarted()
{
  if (file!=NULL)
	return true;
  //
  char c[MAX_PATH];
  strcpy(c,fnexe.c_str());

  BOOL bres = pMapAndLoad(c,0,&image,false,true);
  if (bres)
	ismapped=true;
  else
  {
	err="Failed to load executable '"+fnexe+"'";
	return false;
  }
  modname      = ChangeFileExt(ExtractFileName(fnexe),"");
  szModName    = ((modname.Length()+1)+3) & (~3); // round it up
  oCv          = sizeof(IMAGE_SEPARATE_DEBUG_HEADER) + image.NumberOfSections*sizeof(IMAGE_SECTION_HEADER) + 1*sizeof(IMAGE_DEBUG_DIRECTORY);
  cvoSstModule = sizeof(OMFSignature) + sizeof(OMFDirHeader) + 3*sizeof(OMFDirEntry);
  szSstModule  = offsetof(OMFModule,SegInfo) + image.NumberOfSections*sizeof(OMFSegDesc) + szModName;
  cvoGlobalPub = cvoSstModule + szSstModule;
  gpoSym       = sizeof(OMFSymHash);

  file   = fopen(fndbg.c_str(),"wb");
  if (file==NULL)
  {
	err="Failed to open output file "+fndbg;
	return false;
  }
  return true;
}


bool TDebugFile::AddSymbol(unsigned short seg,unsigned long offset,AnsiString symbol)
{
  EnsureStarted();
  if (file==NULL)
	return false;
  BYTE buffer[512];
  PUBSYM32* pPubSym32 = (PUBSYM32*)buffer;
  // nb. that PSUBSYM32 only works with names up to 255 characters. This
  // code is experimental: I don't know what happens if two symbols
  // get truncated down to the same 255char prefix.
  if (symbol.Length()>255)
	symbol = symbol.SubString(1,255);
  DWORD cbSymbol      = symbol.Length();
  DWORD realRecordLen = sizeof(PUBSYM32) + cbSymbol;
  pPubSym32->reclen   = (unsigned short)(realRecordLen - 2);
  pPubSym32->rectyp   = S_PUB32;
  pPubSym32->off      = offset;
  pPubSym32->seg      = seg;
  pPubSym32->typind   = 0;
  pPubSym32->name[0]  = (unsigned char)cbSymbol;
  lstrcpy( (PSTR)&pPubSym32->name[1], symbol.c_str() );
  fseek(file, oCv + cvoGlobalPub + gpoSym,SEEK_SET );
  fwrite( pPubSym32, realRecordLen, 1, file );
  gpoSym += realRecordLen;
  return true;
}


bool TDebugFile::End()
{
  int numsecs = image.NumberOfSections;
  unsigned long cvoSegMap = cvoGlobalPub + gpoSym;
  unsigned long szSegMap  = sizeof(OMFSegMap) + numsecs*sizeof(OMFSegMapDesc);
  unsigned long szCv      = cvoSegMap + szSegMap;
  if (numsecs>=0xFFFF)
	return false; // OMFSegDesc only uses 'unsigned short'

  EnsureStarted();
  if (file==NULL)
	return false;
  fseek(file,0,SEEK_SET);
  //
  // WriteDBGHeader
  IMAGE_SEPARATE_DEBUG_HEADER isdh;
  isdh.Signature = IMAGE_SEPARATE_DEBUG_SIGNATURE;
  isdh.Flags = 0;
  isdh.Machine            = image.FileHeader->FileHeader.Machine;
  isdh.Characteristics    = image.FileHeader->FileHeader.Characteristics;
  isdh.TimeDateStamp      = image.FileHeader->FileHeader.TimeDateStamp;
  isdh.CheckSum           = image.FileHeader->OptionalHeader.CheckSum;
  isdh.ImageBase          = image.FileHeader->OptionalHeader.ImageBase;
  isdh.SizeOfImage        = image.FileHeader->OptionalHeader.SizeOfImage;
  isdh.NumberOfSections   = numsecs;
  isdh.ExportedNamesSize  = 0;
  isdh.DebugDirectorySize = 1*sizeof(IMAGE_DEBUG_DIRECTORY);
  isdh.SectionAlignment   = image.FileHeader->OptionalHeader.SectionAlignment;
  fwrite( &isdh,sizeof(isdh),1,file);
  //
  // WriteSectionTable
  check(sizeof(IMAGE_SEPARATE_DEBUG_HEADER),"Section table");
  fwrite(image.Sections, sizeof(IMAGE_SECTION_HEADER), numsecs, file);
  //
  // WriteDbgDirectory
  check(sizeof(IMAGE_SEPARATE_DEBUG_HEADER) + numsecs*sizeof(IMAGE_SECTION_HEADER),"Debug directory");
  IMAGE_DEBUG_DIRECTORY idd;
  idd.Characteristics = 0;
  idd.TimeDateStamp = image.FileHeader->FileHeader.TimeDateStamp;
  idd.MajorVersion = 0;
  idd.MinorVersion = 0;
  idd.Type = IMAGE_DEBUG_TYPE_CODEVIEW;
  idd.SizeOfData = szCv;
  idd.AddressOfRawData = 0;
  idd.PointerToRawData = oCv;
  fwrite( &idd, sizeof(idd), 1, file );
  //
  // WriteCV - misc
  check(oCv, "CV data");
  OMFSignature omfsig = { {'N','B','0','9'}, sizeof(omfsig) };
  fwrite( &omfsig, sizeof(omfsig), 1, file );
  // WriteCV - misc - dirheader
  OMFDirHeader omfdirhdr;
  omfdirhdr.cbDirHeader = sizeof(omfdirhdr);
  omfdirhdr.cbDirEntry = sizeof(OMFDirEntry);
  omfdirhdr.cDir = 3;
  omfdirhdr.lfoNextDir = 0;
  omfdirhdr.flags = 0;
  fwrite( &omfdirhdr, sizeof(omfdirhdr), 1, file );
  // WriteCV - misc - direntry[0]: sstModule
  OMFDirEntry omfdirentry;
  omfdirentry.SubSection = sstModule;
  omfdirentry.iMod = 1;
  omfdirentry.lfo = cvoSstModule;
  omfdirentry.cb = szSstModule;
  fwrite( &omfdirentry, sizeof(omfdirentry), 1, file );
  // WriteCV - misc - direntry[1]: sstGlobalPub
  omfdirentry.SubSection = sstGlobalPub;
  omfdirentry.iMod = 0xFFFF;
  omfdirentry.lfo = cvoGlobalPub;
  omfdirentry.cb = gpoSym;
  fwrite( &omfdirentry, sizeof(omfdirentry), 1, file );
  // WriteCV - misc - direntry[2]: sstSegMap
  omfdirentry.SubSection = sstSegMap;
  omfdirentry.iMod = 0xFFFF;
  omfdirentry.lfo = cvoSegMap;
  omfdirentry.cb = szSegMap;
  fwrite( &omfdirentry, sizeof(omfdirentry), 1, file );
  //
  // WriteSstModule
  check(oCv + cvoSstModule, "CV:SST module");
  OMFModule omfmodule;
  omfmodule.ovlNumber = 0;
  omfmodule.iLib = 0;
  omfmodule.cSeg = (unsigned short)numsecs;
  omfmodule.Style[0] = 'C';
  omfmodule.Style[1] = 'V';
  fwrite( &omfmodule, offsetof(OMFModule,SegInfo), 1, file );
  // WriteSstModule - numsecs*OMFSegDesc
  for (int i = 0; i < numsecs; i++ )
  { OMFSegDesc omfsegdesc;
	omfsegdesc.Seg = (unsigned short)(i+1);
	omfsegdesc.pad = 0;
	omfsegdesc.Off = 0;
	omfsegdesc.cbSeg = image.Sections[i].Misc.VirtualSize;
	fwrite( &omfsegdesc, sizeof(omfsegdesc), 1, file );
  }
  // WriteSstModule - modname
  fwrite( modname.c_str(), szModName, 1, file );
  //
  // WriteGlobalPub
  check(oCv + cvoGlobalPub,"CV:GlobalPub module");
  OMFSymHash omfSymHash;
  omfSymHash.cbSymbol = gpoSym - sizeof(OMFSymHash);
  omfSymHash.symhash = 0; // No symbol or address hash tables...
  omfSymHash.addrhash = 0;
  omfSymHash.cbHSym = 0;
  omfSymHash.cbHAddr = 0;
  fwrite( &omfSymHash, sizeof(omfSymHash), 1, file );
  // WriteGlobal - symbols
  fseek(file, oCv + cvoSegMap, SEEK_SET);
  //
  // WriteSegMap
  check(oCv + cvoSegMap,"CV:SegMap module");
  OMFSegMap omfSegMap = {(unsigned short)numsecs,(unsigned short)numsecs};
  fwrite( &omfSegMap, sizeof(OMFSegMap), 1, file );
  // WriteSegMap - nsec*OMFSegMapDesc
  for (int i = 1; i <= numsecs; i++ )
  { OMFSegMapDesc omfSegMapDesc;
	omfSegMapDesc.flags = 0;
	omfSegMapDesc.ovl = 0;
	omfSegMapDesc.group = 0;
	omfSegMapDesc.frame = (unsigned short)i;
	omfSegMapDesc.iSegName = 0xFFFF;
	omfSegMapDesc.iClassName = 0xFFFF;
	omfSegMapDesc.offset = 0;
	omfSegMapDesc.cbSeg = image.Sections[i-1].Misc.VirtualSize;
	fwrite( &omfSegMapDesc, sizeof(OMFSegMapDesc), 1, file );
  }
  //
  check(oCv + szCv,"CV:end");
  return (err == "");
}


//============================================================================
// TMapFile -- for reading a .map file
// methods GetSymbol(seg,off,name)
//============================================================================
// File format: It's a plain text file
// It must be generated with from BCB with 'publics' or 'detailed'.
// Just segments alone isn't enough. It has a load of junk at the top. We're
// interested in the bit that starts with the line
// "  Address         Publics by Value", "", followed by lines of the form
// " 0001:00000000      c1_0" until the end of the file
// If we discover any @ symbols in the function names, that's probably because
// show-mangled-names was turned on
//
class TMapFile
{ public:
  TMapFile(AnsiString fnmap);
  ~TMapFile()
  {
	if (str!=NULL)
	  delete str;
	str=NULL;
  }
  bool GetSymbol(unsigned short *aseg,unsigned long *aoff,AnsiString *aname);
  //
  bool isok;
  bool ismangled;
  int line;
  int num;
  TStringList *str;
  AnsiString err;
};


TMapFile::TMapFile(AnsiString fnmap)
{
  err="";
  isok=false;
  str=new TStringList();
  try
  {
	str->LoadFromFile(fnmap);
  }
  catch (Exception &e)
  {
	err="Couldn't load file '"+fnmap+"' - "+e.ClassName()+" "+e.Message;
  }

  //exact indexof does not work for new Delphi/CBuilder .map files (2007 & 2009)
  //line=str->IndexOf("  Address         Publics by Value");

  String s;
  for (int i=0; i < str->Count; i++)
  {
	s = str->Strings[i];
	if (s.Pos(" Publics by Value") != 0)    //compatible with D2007, 2009 etc
	{
	  line = i;
	  break;
	}
  }
  line++; // to skip past that header
  isok = (line!=0);

  if (line==0 && err!="")
	err="Map file doesn't list any publics - '"+fnmap+"'";
  num=str->Count-line-1;
  ismangled=false;
  for (int i=0; line!=0 && !ismangled && i<str->Count; i++)
  {
	String s = str->Strings[i];
	if (s.Pos("@")!=0)
	  ismangled=true;
  }
}

bool TMapFile::GetSymbol(unsigned short *aseg,unsigned long *aoff,AnsiString *aname)
{
  if (line==0)
	return false;
  if (err!="")
	return false;
  while (line<str->Count && str->Strings[line]=="") line++;
  if (line==str->Count)
	return false;
  AnsiString s = str->Strings[line];
  line++;
  if (s.Length()<22)
	return false;
  AnsiString sseg = s.SubString(2,4);
  for (int i=1; i<=sseg.Length(); i++)
  {
	char c = sseg[i];
	bool okay = (c>='0' && c<='9') || (c>='A' && c<='F') || (c>='a' && c<='f');
	if (!okay)
	  return false;
  }
  AnsiString soff=s.SubString(7,8);
  for (int i=1; i<soff.Length(); i++)
  {
	char c = soff[i];
	bool okay = (c>='0' && c<='9') || (c>='A' && c<='F') || (c>='a' && c<='f');
	if (!okay)
	  return false;
  }
  AnsiString sname = Trim(s.SubString(21,s.Length()-20));
  unsigned int val;
  int i;
  i = sscanf(sseg.c_str(),"%x",&val);
  if (i!=1)
	return false;
  if (val>0xFFFF)
	return false;
  *aseg = (unsigned short)val;
  i = sscanf(soff.c_str(),"%x",&val);
  if (i!=1)
	return false;
  *aoff  = val;
  *aname = sname;
  return true;
}


//============================================================================
// convert -- reads in symbols from a MAP file, writes then out in the DBG
// file, marks the executable as 'debug-stripped'. Or you can tell it not
// to bother reading the map or writing the dbg, but merely mark the executable.
//============================================================================
//
int convert(AnsiString exe,AnsiString &err)
{
  iinit();
  if (!issucc)
	{err="imagehlp.dll could not be loaded";
	 return 0;}
  //
  if (!FileExists(exe))
	{err="File '"+exe+"' does not exist.";
	 return 0;}
  AnsiString dbg = ChangeFileExt(exe,".dbg");
  AnsiString map = ChangeFileExt(exe,".map");
  if (!FileExists(map))
	{err="Need the map file '"+map+"' to get symbols.";
	 return 0;}
  //
  TMapFile *mf = new TMapFile(map);
  int num=mf->num;
  TDebugFile *df = new TDebugFile(exe,dbg);
  bool anymore=true;
  while (anymore)
  { unsigned short seg;
	unsigned long off;
	AnsiString name;
	anymore=mf->GetSymbol(&seg,&off,&name);
	if (anymore)
	  anymore=df->AddSymbol(seg,off,name); // stop it upon error
  }
  delete mf;
  bool dres=df->End();
  AnsiString derr=df->err;
  delete df;
  if (!dres)
	{err=derr;return 0;}

  // Mark it as debug-stripped.
  HANDLE hf = CreateFile(exe.c_str(),GENERIC_READ|GENERIC_WRITE,0,NULL,OPEN_EXISTING,0,NULL); DWORD red;
  if (hf==INVALID_HANDLE_VALUE)
	{err="Unable to open '"+exe+"' to strip it of debugging information. "+le();
	 return 0;}
  IMAGE_DOS_HEADER idh;
  ReadFile(hf,&idh,sizeof(idh),&red,NULL);
  if (red!=sizeof(idh))
	{err="Unable to open '"+exe+"' header to strip it of debugging information.";
	 return 0;}
  DWORD off = idh.e_lfanew+sizeof(DWORD);
  red=SetFilePointer(hf,off,NULL,FILE_BEGIN);
  if (red!=off)
	{err="Unable to find header of '"+exe+"' to strip it of debugging information.";
	 return 0;}
  IMAGE_FILE_HEADER pfh;
  ReadFile(hf,&pfh,sizeof(pfh),&red,NULL);
  if (red!=sizeof(pfh))
	{err="Unable to read header of '"+exe+"' to strip it of debugging information.";
	 return 0;}
  bool already = ((pfh.Characteristics & IMAGE_FILE_DEBUG_STRIPPED)>0);
  if (!already)
  { pfh.Characteristics |= IMAGE_FILE_DEBUG_STRIPPED;
	SetFilePointer(hf,off,NULL,FILE_BEGIN);
	WriteFile(hf,&pfh,sizeof(pfh),&red,NULL);
  }
  CloseHandle(hf);
  //
  err="";
  return num;
}
