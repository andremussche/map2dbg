//---------------------------------------------------------------------------

#include <vcl.h>
#pragma hdrstop

#include <windows.h>
#include <imagehlp.h>
#include <stdio.h>
#include <system.hpp>
#include <sysutils.hpp>
#include <classes.hpp>
#include "cvexefmt.h"
#include "convert.h"

#include <tchar.h>
//---------------------------------------------------------------------------

#pragma argsused
int _tmain(int argc, _TCHAR* argv[])
{
  bool ok = (argc==2);
  if (argc==3 && argv[2]=="/nomap")
	ok=true;
  if (!ok)
  {
	fputs("Map2Dbg version 1.4\n",stdout);
	fputs("Syntax: map2dbg [/nomap] file.exe\n",stdout);
	return 1;
  }

  AnsiString exe=argv[1];
  if (argc==3)
	exe=argv[2];
  if (!FileExists(exe) && FileExists(exe+".exe"))
	exe=exe+".exe";
  if (!FileExists(exe) && FileExists(exe+".dll"))
	exe=exe+".dll";
  if (!FileExists(exe))
  {
	fputs(("File '"+exe+"' not found").c_str(),stdout);
	return 1;
  }

  AnsiString err;
  int num = convert(exe,err);

  if (err=="")
  {
	fputs(("Converted "+AnsiString(num)+" symbols.").c_str(),stdout);
	return 0;
  }
  else
  {
	fputs(err.c_str(),stdout);
	return 1;
  }
}
//---------------------------------------------------------------------------

