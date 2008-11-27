#include <windows.h>
#include <imagehlp.h>
#include <tlhelp32.h>
#include <vcl.h>
#pragma hdrstop
#include "callstack.h"

#include "mainform.h"
//---------------------------------------------------------------------------
#pragma package(smart_init)
#pragma resource "*.dfm"
TForm1 *Form1;
//---------------------------------------------------------------------------
__fastcall TForm1::TForm1(TComponent* Owner)
        : TForm(Owner) 
{       
}
//---------------------------------------------------------------------------

void __fastcall TForm1::bCallstackClick(TObject *Sender)
{
  Memo1->Text = dcallstack();
}
//---------------------------------------------------------------------------



