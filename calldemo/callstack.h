#ifndef callstackH
#define callstackH

AnsiString __fastcall dcallstack();
AnsiString ShowCallstack(HANDLE hThread, CONTEXT *context);

#endif
