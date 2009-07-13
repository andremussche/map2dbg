unit muConvertToDBG;

interface

uses
  Classes, JwaImageHlp, JwaWinNT,
  Windows, JclDebugExt, JclPeImage;

// Define the Converter class that will create the .dbg file
type
  TConverter = class(TObject)
  private
    FMemoryStream:    TMemoryStream;
    sFileName,
    sModName,
    sDebugFile:       String;
    bIsMapped:        BOOL;
    wModNameLength:   Word;
    lOffCV,
    lOffCVSstMod,
    lSizeSstMod,
    lOffCVGlobalPub,
    lOffGlobalPubSym: LongWord;
    tImage:           _LOADED_IMAGE;
  public
    procedure AfterConstruction;override;
    destructor Destroy;override;
    procedure DeleteTempFiles;
    procedure ConvertToDBG;
    function  ConvertMapToDBG(aDebugInfoSource:TJclDebugInfoSource): BOOL;
    function  ConvertJDBGToDBG(tDebugInfoSource:TJclDebugInfoSource): BOOL;
    function  LastPos(aSubStr, aString :string): Integer;
    function  EnsureStarted : BOOL;
    function  CreateDBG : BOOL;
    procedure WriteHeader;
    procedure InitHeaderUsingMap(aDebugInfoSource: TJclDebugInfoSource);
    procedure InitHeaderUsingJDBG(aDebugInfoSource: TJclDebugInfoSource);
    procedure WriteSymbol(aSeg, aOff:Integer; const aSymbol:string);
    procedure CloseDBG;
    procedure MarkAsDebugStriped;
  end;

  //ttest = string[1];
  TPubSym32 = packed record
    wRecLen:  Word;
    iRecTyp:  Word;
    lOff:     LongWord;
    bSeg:     Word;
    bTypInd:  Word;
    cName:    Char;
  end;
  PPubSym32 = ^TPubSym32;

  TOMFSignature = record
    cSignature: array [0..3] of Char;
    lFilePos:   LongInt;
  end;
  POMFSignature = ^TOMFSignature;

  TOMFDirHeader = record
    bDirHeader,
    bDirEntry:  Word;
    lDir:       LongWord;
    lNextDir:   LongInt;
    lFlags:     LongWord;
  end;
  POMFDirHeader = ^TOMFDirHeader;

  TOMFDirEntry = record
    iSubSection,
    iModInd:     Word;
    lFileOff:    LongInt;
    lByteCount:  LongWord;
  end;
  POMFDirEntry = ^TOMFDirEntry;

  TOMFSegDesc = record
    bSeg,
    bPad:       Word;
    lOff,
    lByteCount: LongWord;
  end;
  POMFSegDesc = ^TOMFSegDesc;

  TOMFSegMapDesc = record
    bFlags,
    bOverlay,
    bGroup,
    bFrame:         Word;
    iSegNameInd,
    iClassNameInd:  Word;
    lOff,
    lSegByteCount:  LongWord;  
  end;
  POMFSegMapDesc = ^TOMFSegMapDesc;

  TOMFSegMap = record
    bSeg,
    bSegLog:      Word;
    rSegMapDesc:  array [0..0] of POMFSegMapDesc;
  end;
  POMFSegMap = ^TOMFSegMap;
  
  TOMFModule = record
    bOverlayNr,
    bLib,
    bSeg:       Word;
    cStyle:     array [0..1]   of Char;
    rSegInfo:   array [0..0]   of TOMFSegDesc;
  end;
  POMFModule = ^TOMFModule;

  TOMFSymHash = record
    bSymHash,
    bAddrHash:    Word;
    lSymLen,
    lSymHashLen,
    lAddrHashLen: LongWord;
  end;
  POMFSymHash = ^TOMFSymHash;

var
  GConverter: TConverter;

implementation

uses
  JclFileUtils,
  Messages, Dialogs, SysUtils, Forms, JclStrings;

const
  sstModule          = $120;
  sstTypes           = $121;
  sstPublic          = $122;
  sstPublicSym       = $123;   // publics as symbol (waiting for link);
  sstSymbols         = $124;
  sstAlignSym        = $125;
  sstSrcLnSeg        = $126;   // because link doesn't emit SrcModule
  sstSrcModule       = $127;
  sstLibraries       = $128;
  sstGlobalSym       = $129;
  sstGlobalPub       = $12a;
  sstGlobalTypes     = $12b;
  sstMPC             = $12c;
  sstSegMap          = $12d;
  sstSegName         = $12e;
  sstPreComp         = $12f;   // precompiled types
  sstPreCompMap      = $130;   // map precompiled types in global types
  sstOffsetMap16     = $131;
  sstOffsetMap32     = $132;
  sstFileIndex       = $133;   // Index of file names
  sstStaticSym       = $134;
  
procedure TConverter.AfterConstruction;
begin
  inherited;
  FMemoryStream := nil;
  bIsMapped     := False;
end;

destructor TConverter.Destroy;
begin
  FMemoryStream.Free;
  inherited;
end;

// Deletes the tmp files that were created during the converting proces
procedure TConverter.DeleteTempFiles;
var
  sTmpExeExtension,
  sAppName,
  sExeName,
  sTmpName: string;
begin
  sTmpExeExtension      := '.tmpExe';

  sAppName  := Application.ExeName;
  sExeName  := ExtractFileName(sAppName);

  // Check if params have been given
  if ParamCount > 0 then
  begin
    sTmpName:= sExeName + sTmpExeExtension;
    DeleteFile(sTmpName);
  end;

end;

// Take the available debug format and convert it to a .dbg file
procedure TConverter.ConvertToDBG;
var
  sAppName: string;
  iPos,
  iDebugInfoSourceID:   Integer;
  bState:               BOOL;
  tDebugInfoSource:     TJclDebugInfoSource;
  lInfoSourceClassList: TList;
begin
  sAppName  := Application.ExeName;
  sFileName := ExtractFileName(sAppName);
  //Extract modname
  iPos      := LastPos('.', sFileName);
  sModName  := Copy(sFileName,0, iPos-1);

  // Check which debug format is present and try to convert it to .dbg
  lInfoSourceClassList := TList.Create;
  {$IFNDEF DEBUG_NO_BINARY}
  lInfoSourceClassList.Add(Pointer(TJclDebugInfoBinary));
  {$ENDIF !DEBUG_NO_BINARY}
  {$IFNDEF DEBUG_NO_MAP}
  lInfoSourceClassList.Add(Pointer(TJclDebugInfoMap));
  {$ENDIF !DEBUG_NO_MAP}
  {$IFNDEF DEBUG_NO_TD32}
  lInfoSourceClassList.Add(Pointer(TJclDebugInfoTD32));
  {$ENDIF !DEBUG_NO_TD32}
  {$IFNDEF DEBUG_NO_EXPORTS}
  lInfoSourceClassList.Add(Pointer(TJclDebugInfoExports));
  {$ENDIF !DEBUG_NO_EXPORTS}

  tDebugInfoSource := nil;
  for iDebugInfoSourceID := 0 to lInfoSourceClassList.Count - 1 do
  begin
    tDebugInfoSource := TJclDebugInfoSourceClass(lInfoSourceClassList.Items[iDebugInfoSourceID]).Create(HInstance);
    try
      if tDebugInfoSource.InitializeSource then
        Break
      else
        FreeAndNil(tDebugInfoSource);
    except
      tDebugInfoSource.Free;
      raise;
    end; // TRY
  end; // FOR

  if tDebugInfoSource <> nil then
    begin
      if tDebugInfoSource is TJclDebugInfoBinary then
      begin
        bState := ConvertJDBGToDBG(tDebugInfoSource);
        if bState = False then
          begin
            raise Exception.Create('Converting JDBG to DBG failed! Program Exit!');
          end
      end // IF TJclDebugInfoBinary
      else if tDebugInfoSource is TJclDebugInfoMap then
        begin
          bState := ConvertMapToDBG(tDebugInfoSource);
          if bState = False then
            begin
              raise Exception.Create('Converting Map to DBG failed! Program Exit!');
            end
        end // IF TJclDebugInfoMap
       else
        begin
          raise Exception.Create('Unsupported debug format found! Program Exit!');
        end
      end // IF DebugInfoSource <> nil
  else
    begin
      raise Exception.Create('No Debugformat found! Program Exit');
      Halt(0);
    end;
  MarkAsDebugStriped;
end; // ConvertToDBG

// Convert .map to .dbg
function TConverter.ConvertMapToDBG(aDebugInfoSource: TJclDebugInfoSource) : BOOL;
var
  tScanner:         TJclMapScanner;
  tProcName:        TJclMapProcName;
  sSymbol,
  sMapName:         string;
  bState:           BOOL;
  iProcNameID,
  iProcNameCount:   Integer;

  function __ExtractSymbol(aString: PJclMapString): String;
  var
    P: PChar;
  begin
    if aString = nil then
    begin
      Result := '';
      Exit;
    end;
    if aString^ = '(' then
    begin
      Inc(aString);
      P := aString;
      while not (P^ in [AnsiCarriageReturn, ')']) do
        Inc(P);
    end
    else
    begin
      P := aString;
      while not (P^ in [AnsiCarriageReturn, '(']) do
        Inc(P)
    end;
    SetString(Result, aString, P - aString);
  end;

begin
  sMapName := ChangeFileExt(sFileName, '.map');
  tScanner := TJclDebugInfoMap(aDebugInfoSource).GetScanner();

  if FileExists(sMapName) then
  begin
    bState    := CreateDBG;
    if bState = False then
      begin
        raise Exception.Create('Creating DBG file failed! Program exit!');
      end;

    InitHeaderUsingMap(aDebugInfoSource);
    WriteHeader;

    iProcNameCount  := High(tScanner.FProcNames);
    for iProcNameID := 0 to iProcNameCount do
      begin
        tProcName := tScanner.FProcNames[iProcNameID];
        sSymbol   := __ExtractSymbol(tProcName.ProcName);
        WriteSymbol(tProcName.Segment, tProcName.VA, sSymbol);
      end; // FOR ProcNames

    CloseDBG;
    Result := True; // SUCCESS
  end // IF FileExists
  else
  begin
    Result := False; // FAILURE
  end;
end; // Convert Map to DBG

type
  TJclBinDebugScannerHack = class(TJclBinDebugScanner);

// Convert .jdbg to .dbg
function TConverter.ConvertJDBGToDBG(tDebugInfoSource : TJclDebugInfoSource) : BOOL;
var
  sJdbgFileName:    TFileName;
  tFStream:         TCustomMemoryStream;
  tScanner:         TJclBinDebugScanner;
  tProc:            TJclBinDbgNameCache;
  tSeg:             TJclBinDbgSegmentCache;
  sAppName,
  sProcName:        string;
  iProcNameID,
  iSegmentID,
  iSegmentsHigh,
  iOffset:          Integer;
  bState:           BOOL;
begin
  sAppName      := Application.ExeName;
  sJdbgFileName := ChangeFileExt(sAppName, JclDbgFileExtension);
  tFStream      := nil;

  if FileExists(sJdbgFileName) then
  begin
    tFStream := TJclFileMappingStream.Create(sJdbgFileName, fmOpenRead or fmShareDenyWrite);
  end
  else
  begin
    Result := (PeMapImgFindSectionFromModule(Pointer(HInstance), JclDbgDataResName) <> nil);
    if Result then
      tFStream := TJclPeSectionStream.Create(HInstance, JclDbgDataResName)
  end;

  if tFStream <> nil then
    begin
      tScanner := TJclBinDebugScanner.Create(tFStream, True);
      TJclBinDebugScannerHack(tScanner).CacheProcNames;
      TJclBinDebugScannerHack(tScanner).CacheLineNumbers;
      //tScanner.ProcNameFromAddr( Cardinal(@Windows.Beep) );
      //tScanner.LineNumberFromAddr( Cardinal(@Windows.Beep));
      //tScanner.LineNumberFromAddr( 0 );

      bState    := CreateDBG;
      if bState = False then
        begin
          raise Exception.Create('Creating DBG file failed! Program exit!');
        end;

      InitHeaderUsingJDBG(tDebugInfoSource);
      WriteHeader;

      for iProcNameID := 0 to High(tScanner.FProcNames) do
        begin
          tProc     := tScanner.FProcNames[iProcNameID];
          sProcName := tScanner.ProcNameFromAddr(tProc.Addr);

          {$IFDEF COMPATIBILITY}  // For usage of JclDebug without segments (JclDebugExt)
            iOffset := tProc.Addr;
            WriteSymbol(1, iOffset, sProcName);
          {$ENDIF COMPATIBILITY}

          {$IFNDEF COMPATIBILITY} // For usage of JclDebug with segments (JclDebugExt)
            // Determine proper segment based upon address
            iSegmentsHigh := High(tScanner.FSegmentNames);
            for iSegmentID := 0 to iSegmentsHigh do
              begin
                tSeg := tScanner.FSegmentNames[iSegmentID];
                if  (tProc.Addr >= tSeg.Address)
                and (tProc.Addr < (tSeg.Address + tSeg.Length)) then
                    Break;
              end; // FOR Segments

            iOffSet := tProc.Addr;
            //iOffSet := tProc.Addr - tSeg.Address;
            WriteSymbol(tSeg.Segment, iOffset, sProcName);
          {$ENDIF !COMPATIBILITY}
          
        end; // FOR ProcNames

      CloseDBG;
      Result := True; // SUCCESS
    end // IF FileExists
  else
    begin
       Result := False; // FAILURE
    end;
end; // Convert JDBG to DBG

// Returns the last position of the substring in the string
function TConverter.LastPos(aSubStr: string; aString: string) : Integer;
var
  iFound,
  iLen,
  iPos: Integer;
begin
  iPos := Length(aString);
  iLen := Length(aSubStr);
  iFound := 0;
  while (iPos > 0) and (iFound = 0) do
  begin
    if Copy(aString, iPos, iLen) = aSubStr then
      iFound := iPos;
    Dec(iPos);
  end;
  Result := iFound;
end;

// Make sure the writing process is properly started
function TConverter.EnsureStarted : BOOL;
var
  bRes:       BOOL;
//  rOMFModule:       POMFModule;
  iModRes,
  iModNameLength:   Integer;
  sModName:         String;
begin
  if FMemoryStream <> nil then
    begin
      Result := True;
      Exit;
    end;

  FMemoryStream := TMemoryStream.Create;
  bRes          := MapAndLoad(PCHAR(sFileName), nil, tImage, False, True);

  if  bRes = True then
    begin
      bIsMapped := True;
    end
  else
    begin
      raise Exception.Create('Failed to load executable into an image!');
    end;

  sModName                := ChangeFileExt(sFileName,'');
  iModNameLength          := Length(sModName);
  iModRes                 := iModNameLength Mod 4;
  wModNameLength          := iModNameLength;
  if (iModRes > 0) then
  begin
    wModNameLength        := wModNameLength + (4 - iModRes);
  end;
  lOffCV                  := SizeOf(IMAGE_SEPARATE_DEBUG_HEADER) + tImage.NumberOfSections * SizeOf(IMAGE_SECTION_HEADER) + SizeOf(IMAGE_DEBUG_DIRECTORY);
  lOffCVSstMod            := SizeOf(TOMFSignature) + SizeOf(TOMFDirHeader) + 3 * SizeOf(TOMFDirEntry);
  lSizeSstMod             := 8 + tImage.NumberOfSections * SizeOf(TOMFSegDesc) + wModNameLength;
  lOffCVGlobalPub         := lSizeSstMod + lOffCVSstMod;
  lOffGlobalPubSym        := SizeOf(TOMFSymHash);

  FMemoryStream.Position  := 0;
  FMemoryStream.SetSize(1024*1024);
  FMemoryStream.Clear;
  Result := True;
end;

// Start the dbg
function TConverter.CreateDBG : BOOL;
begin
  sDebugFile  := ChangeFileExt(sFileName, '.dbg');
  Result      := EnsureStarted;
end;

// Write the header of the dbg file
procedure TConverter.WriteHeader;
var
  iNumSecs,
  iSecID:         Integer;
  lOffCVSegMap,
  lSizeSegMap,
  lSizeCV:        LongWord;
  hISDH:          PIMAGE_SEPARATE_DEBUG_HEADER;
  hIDD:           PIMAGE_DEBUG_DIRECTORY;
//  rOMFSegMap:     POMFSegMap;
//  rOMFSegMapDesc: POMFSegMapDesc;
  rOMFSegDesc:    POMFSegDesc;
  rOMFSignature:  POMFSignature;
  rOMFDirHeader:  POMFDirHeader;
  rOMFDirEntry:   POMFDirEntry;
  rOMFModule:     POMFModule;
  rOMFSymHash:    POMFSymHash;
  section:        PIMAGE_SECTION_HEADER;
begin
  iNumSecs      := tImage.NumberOfSections;
  lOffCVSegMap  := lOffCVGlobalPub + lOffGlobalPubSym;
  lSizeSegMap   := SizeOf(POMFSegMap) + iNumSecs * 20;
  lSizeCV       := lOffCVSegMap + lSizeSegMap;

  if iNumSecs >= $FFFF then
    begin
      raise Exception.Create('Too much segments!');
    end;

  if (EnsureStarted = False) or (FMemoryStream = nil) then
    begin
      raise Exception.Create('MemoryStream was not opened correctly!');
    end;

  // WriteImageHeader;

  FMemoryStream.Clear;
  // Write Debug Header
  GetMem(hISDH, SizeOf(IMAGE_SEPARATE_DEBUG_HEADER));
  FillChar(hISDH^, SizeOf(IMAGE_SEPARATE_DEBUG_HEADER), 0);
  with hISDH^ do
  begin
    Signature           := IMAGE_SEPARATE_DEBUG_SIGNATURE;
    Flags               := 0;
    Machine             := tImage.FileHeader.FileHeader.Machine;
    Characteristics     := tImage.FileHeader.FileHeader.Characteristics;
    TimeDateStamp       := tImage.FileHeader.FileHeader.TimeDateStamp;
    CheckSum            := tImage.FileHeader.OptionalHeader.CheckSum;
    ImageBase           := tImage.FileHeader.OptionalHeader.ImageBase;
    SizeOfImage         := tImage.FileHeader.OptionalHeader.SizeOfImage;
    NumberOfSections    := iNumSecs;
    ExportedNamesSize   := 0;
    DebugDirectorySize  := SizeOf(IMAGE_DEBUG_DIRECTORY);
    SectionAlignment    := tImage.FileHeader.OptionalHeader.SectionAlignment;
  end;
  FMemoryStream.Write(hISDH^, SizeOf(IMAGE_SEPARATE_DEBUG_HEADER));

  // Write Section Table
  FMemoryStream.Write(tImage.Sections^, SizeOf(IMAGE_SECTION_HEADER) * iNumSecs);

  // Write Debug Directory
  GetMem(hIDD, SizeOf(IMAGE_DEBUG_DIRECTORY));
  //FillChar(hIDD^, SizeOf(IMAGE_DEBUG_DIRECTORY), 0);
  with hIDD^ do
  begin
    Characteristics      := 0;
    TimeDateStamp        := tImage.FileHeader.FileHeader.TimeDateStamp;
    MajorVersion         := 0;
    MinorVersion         := 0;
    Type_                := IMAGE_DEBUG_TYPE_CODEVIEW;
    SizeOfData           := lSizeCV;
    AddressOfRawData     := 0;
    PointerToRawData     := lOffCV;
  end;
  FMemoryStream.Write(hIDD^, SizeOf(IMAGE_DEBUG_DIRECTORY));

  // Write CV - MISC
  GetMem(rOMFSignature, 8);
  with rOMFSignature^ do
  begin
    cSignature[0] := 'N';
    cSignature[1] := 'B';
    cSignature[2] := '0';
    cSignature[3] := '9';
    lFilePos   := 8;
  end;
  FMemorystream.Write(rOMFSignature^, 8);

  // Write CV - MISC - DIR HEADER
  GetMem(rOMFDirHeader, SizeOf(rOMFDirHeader^));
  with rOMFDirHeader^ do
  begin
    bDirHeader := SizeOf(rOMFDirHeader^);
    bDirEntry  := SizeOf(rOMFDirEntry^);
    lDir       := 3;
    lNextDir   := 0;
    lFlags     := 0;
  end;
  FMemoryStream.Write(rOMFDirHeader^, SizeOf(rOMFDirHeader^));

  // Write CV - MISC - DIR ENTRY[0] SstModule
  GetMem(rOMFDirEntry, SizeOf(TOMFDirEntry));
  with rOMFDirEntry^ do
  begin
    iSubSection := sstModule;
    iModInd     := 1;
    lFileOff    := lOffCVSstMod;
    lByteCount  := lSizeSstMod;
  end;
  FMemoryStream.Write(rOMFDirEntry^, SizeOf(rOMFDirEntry^));

  // Write CV - MISC - DIR ENTRY[1] SstGlobalPub
  GetMem(rOMFDirEntry, SizeOf(rOMFDirEntry^));
  with rOMFDirEntry^ do
  begin
    iSubSection := sstGlobalPub;
    iModInd     := $FFFF;
    lFileOff    := lOffCVGlobalPub;
    lByteCount  := lOffGlobalPubSym;
  end;
  FMemoryStream.Write(rOMFDirEntry^, SizeOf(rOMFDirEntry^));

  // Write CV - MISC - DIR ENTRY[2] SstSegMap
  GetMem(rOMFDirEntry, SizeOf(rOMFDirEntry^));
  with rOMFDirEntry^ do
  begin
    iSubSection := sstSegMap;
    iModInd     := $FFFF;
    lFileOff    := lOffCVSegMap;
    lByteCount  := lSizeSegMap;
  end;
  FMemoryStream.Write(rOMFDirEntry^, SizeOf(rOMFDirEntry^));

  // Write SstModule
  GetMem(rOMFModule, 8);
  FillChar(rOMFModule^, SizeOf(rOMFModule^), 0);
  with rOMFModule^ do
  begin
    bOverlayNr  := 0;
    bLib        := 0;
    bSeg        := iNumSecs;
    cStyle[0]   := 'C';
    cStyle[1]   := 'V';
  end;
  FMemoryStream.Write(rOMFModule^, 8);
  
  // Write SstModule - iNumSecs * rOMFSegDesc
  for iSecID := 0 to iNumSecs -1 do
    begin
      GetMem(rOMFSegDesc, SizeOf(rOMFSegDesc^));
      with rOMFSegDesc^ do
      begin
        bSeg        := iSecID + 1;
        bPad        := 0;
        lOff        := 0;
        section     := PIMAGE_SECTION_HEADER( Cardinal(tImage.Sections) + (iSecID * SizeOf(IMAGE_SECTION_HEADER) ) );
        lByteCount  := section.Misc.VirtualSize;
      end;
      FMemoryStream.Write(rOMFSegDesc^, SizeOf(rOMFSegDesc^));
    end;

  // Write SstModule - sModName
  FMemoryStream.Write(sModName[1], wModNameLength);

  // Write GlobalPub
  GetMem(rOMFSymHash, SizeOf(rOMFSymHash^));
  with rOMFSymHash^ do
  begin
    lSymLen       := lOffGlobalPubSym - SizeOf(TOMFSymHash);
    bSymHash      := 0;
    bAddrHash     := 0;
    lSymHashLen   := 0;
    lAddrHashLen  := 0;
  end;
  FMemoryStream.Write(rOMFSymHash^, SizeOf(rOMFSymHash^));
end;

// Init header, some values needs to be inited before they can be written to the header
procedure TConverter.InitHeaderUsingMap(aDebugInfoSource: TJclDebugInfoSource);
var
  tScanner:         TJclMapScanner;
  tProcName:        TJclMapProcName;
  sAppName,
  sMapName:         string;
//  bState:           BOOL;
  iProcNameID,
  iProcNameCount:   Integer;
  bSymLen:          Byte;
  wRealRecLen:      Word;
//  rPubSym32:        PPubSym32;
  sTName:           string;

  function __NextItemPos(aString: PJclMapString; var aName : String): integer;
  var
    P: PChar;
  begin
    if aString = nil then
    begin
      Result := 0;
      Exit;
    end;
    if aString^ = '(' then
    begin
      Inc(aString);
      P := aString;
      while not (P^ in [AnsiCarriageReturn, ')']) do
        Inc(P);
    end
    else
    begin
      P := aString;
      while not (P^ in [AnsiCarriageReturn, '(']) do
        Inc(P)
    end;
    Result := P - aString;
    SetString(aName, aString, Result);
  end;

begin
  sAppName := Application.ExeName;
  sMapName := ChangeFileExt(sAppName, '.map');
  tScanner := TJclDebugInfoMap(aDebugInfoSource).GetScanner();

  // Init the lOffGlobalPubSym
  iProcNameCount  := High(tScanner.FProcNames);
  for iProcNameID := 0 to iProcNameCount do
    begin
      tProcName   := tScanner.FProcNames[iProcNameID];
      sTName      := '';
      bSymLen     := __NextItemPos(tProcName.ProcName, sTName);
      if bSymLen <= 0 then
        bSymLen   := Length(tProcName.ProcName);
      //if bSymLen > 255 then
      //  bSymLen   := 255;
      wRealRecLen := SizeOf(TPubSym32) + bSymLen;

      lOffGlobalPubSym := lOffGlobalPubSym + wRealRecLen;
    end; // FOR ProcNames

  lOffGlobalPubSym := lOffGlobalPubSym + 28;
end;

// Init header, some values needs to be inited before they can be written to the header
procedure TConverter.InitHeaderUsingJDBG(aDebugInfoSource: TJclDebugInfoSource);
var
  sJdbgFileName:    TFileName;
  tFStream:         TCustomMemoryStream;
  tScanner:         TJclBinDebugScanner;
  tProc:            TJclBinDbgNameCache;
  sProcName,
  sAppName:         string;
//  iSegmentID,
  iProcNameID:      Integer;
//  bSymLen:          Byte;
  wRealRecLen:      Word;
begin
  sAppName      := Application.ExeName;
  sJdbgFileName := ChangeFileExt(sAppName, JclDbgFileExtension);

  tFStream := nil;
  if FileExists(sJdbgFileName) then
  begin
    tFStream := TJclFileMappingStream.Create(sJdbgFileName, fmOpenRead or fmShareDenyWrite);
  end
  else
  begin
    if (PeMapImgFindSectionFromModule(Pointer(HInstance), JclDbgDataResName) <> nil) then
      tFStream := TJclPeSectionStream.Create(HInstance, JclDbgDataResName)
  end;
  assert(tFStream <> nil);  

  tScanner := TJclBinDebugScanner.Create(tFStream, True);
  TJclBinDebugScannerHack(tScanner).CacheProcNames;
  //tScanner.ProcNameFromAddr(0);
  //tScanner.LineNumberFromAddr(0);

  for iProcNameID := 0 to High(tScanner.FProcNames) do
    begin
      tProc             := tScanner.FProcNames[iProcNameID];
      sProcName         := tScanner.ProcNameFromAddr(tProc.Addr);
      wRealRecLen       := SizeOf(TPubSym32) + Length(sProcName);
      lOffGlobalPubSym  := lOffGlobalPubSym + wRealRecLen;
    end; // FOR ProcName

  lOffGlobalPubSym := lOffGlobalPubSym + 28;
end;

// Write a segment, offset and symbol to the file, returns if writiong was successfull
procedure TConverter.WriteSymbol(aSeg: Integer; aOff: Integer; const aSymbol: string);
var
  bSymLen:      Byte;
  wRealRecLen:  Word;
  rPubSym32:    PPubSym32;
  pName:        pchar;

  {function __NextItemPos(aString: PJclMapString; var aName : String): integer;
  var
    P: PChar;
  begin
    if aString = nil then
    begin
      Result := 0;
      Exit;
    end;
    if aString^ = '(' then
    begin
      Inc(aString);
      P := aString;
      while not (P^ in [AnsiCarriageReturn, ')',#0]) do
        Inc(P);
    end
    else
    begin
      P := aString;
      while not (P^ in [AnsiCarriageReturn, '(',#0]) do
        Inc(P)
    end;
    Result := P - aString;
    SetString(aName, aString, Result);
  end;}

begin
  if (EnsureStarted = False) or (FMemoryStream = nil) then
    begin
      raise Exception.Create('MemoryStream is empty or dbg has created yet!');
    end;

  //sTName      := '';
  //bSymLen     := __NextItemPos(aSymbol, sTName);
  //if bSymLen <= 0 then
  bSymLen   := Length(aSymbol);
  wRealRecLen := SizeOf(TPubSym32) + bSymLen;

  GetMem(rPubSym32, wRealRecLen);
  with rPubSym32^ do
  begin
    wRecLen  := wRealRecLen - 2;
    iRecTyp  := $0203;
    lOff     := aOff;
    bSeg     := aSeg;
    bTypInd  := 0;
    //cName[0] := char(bSymLen);
    cName    := char(bSymLen);
    pName    := @cName;
    pName    := Pointer( Cardinal(pName) + SizeOf(Char) );
    Move(aSymbol[1], pName^, bSymLen);
    //Move(sTName[1], pName^, bSymLen);
    //Move(sTName[1], cName[1], bSymLen);
  end;

  //optimalization: increase alloc with 100kb instead of each time
  if (FMemoryStream.Position + wRealRecLen) > FMemoryStream.Size then
    FMemoryStream.SetSize( FMemoryStream.Size + 100 * 1024);

  FMemoryStream.Write(rPubSym32^, wRealRecLen);
end;

procedure TConverter.CloseDBG;
var
  iNumSecs,
  iSecID:         Integer;
  lOffCVSegMap:   LongWord;
  rOMFSegMap:     POMFSegMap;
  rOMFSegMapDesc: POMFSegMapDesc;
//  rOMFSegDesc:    POMFSegDesc;
  section:        PIMAGE_SECTION_HEADER;
begin
  iNumSecs      := tImage.NumberOfSections;
  //lOffCVSegMap  := lOffCVGlobalPub + lOffGlobalPubSym;

  // Write SegMap
  GetMem(rOMFSegMap, SizeOf(rOMFSegMap^));
  with rOMFSegMap^ do
  begin
    bSeg    := iNumSecs;
    bSegLog := iNumSecs;
  end;

  if (FMemoryStream.Position + SizeOf(rOMFSegMap^)) > FMemoryStream.Size then
    FMemoryStream.SetSize(FMemoryStream.Size + 100 * 1024);

  FMemoryStream.Write(rOMFSegMap^, SizeOf(rOMFSegMap^));

  // Write SegMap - iNumSecs * rOMFSegMapDesc
  for iSecID := 1 to iNumSecs do
    begin
      GetMem(rOMFSegMapDesc, SizeOf(rOMFSegMapDesc));
      with rOMFSegMapDesc^ do
      begin
        bFlags        := 0;
        bOverlay      := 0;
        bGroup        := 0;
        bFrame        := iSecID;
        iSegNameInd   := $FFFF;
        iClassNameInd := $FFFF;
        lOff          := 0;
        section       := PIMAGE_SECTION_HEADER( Cardinal(tImage.Sections) + ((iSecID-1) * SizeOf(IMAGE_SECTION_HEADER) ) );
        lSegByteCount := section.Misc.VirtualSize;
      end;

      if (FMemoryStream.Position + SizeOf(rOMFSegMapDesc^)) > FMemoryStream.Size then
        FMemoryStream.SetSize(FMemoryStream.Size + 100 * 1024);

      FMemoryStream.Write(rOMFSegMapDesc^, SizeOf(rOMFSegMapDesc^));
    end;

  // Write memorystream to file
  FMemoryStream.SaveToFile( ChangeFileExt(sModName, '.dbg') );
end;

procedure TConverter.MarkAsDebugStriped;
var
  fileHandle: THandle;
  idh:        IMAGE_DOS_HEADER;
  ifh:        IMAGE_FILE_HEADER;
  red,
  off:        DWORD;
  already:    Bool;
  tmpFileName:    string;
begin
  tmpFileName := ChangeFileExt(sFileName, '.tmpExe');
  CopyFile(PChar(sFileName), PChar(tmpFileName), False);

  fileHandle  := CreateFile(PChar(tmpFileName), GENERIC_READ or GENERIC_WRITE, 0, NIL, OPEN_ALWAYS, 0, 0);
  if fileHandle = INVALID_HANDLE_VALUE then
    raise Exception.Create('Unable to open executable to strip it for debug information!');

  ReadFile(fileHandle, idh, SizeOf(idh), red, NIL);
  if red <> SizeOf(idh) then
    raise Exception.Create('Unable to open executable header to strip it for debug information!');

  off := idh._lfanew + SizeOf(DWORD);
  red := SetFilePointer(fileHandle, off, NIL, FILE_BEGIN);
  if red <> off then
    raise Exception.Create('Unable to find executable header to strip it for debug information!');

  ReadFile(fileHandle, ifh, SizeOf(ifh), red, NIL);
  if red <> SizeOf(ifh) then
    raise Exception.Create('Unable to read executable header to strip it for debug information!');

  already := (ifh.Characteristics and IMAGE_FILE_DEBUG_STRIPPED) > 0;
  if already = False then
  begin
    ifh.Characteristics := ifh.Characteristics or IMAGE_FILE_DEBUG_STRIPPED;
    SetFilePointer(fileHandle, off, NIL, FILE_BEGIN);
	  WriteFile(fileHandle, ifh, SizeOf(ifh), red, NIL);
  end;

  CopyFile(PChar(tmpFileName), PChar(sFileName), False);
end;

initialization

  // DEBUG
  if  ParamCount > 0 then
  begin
    if LowerCase(ParamStr(1)) = '-converttodbg' then
    begin
      GConverter := TConverter.Create;
      // Cleanup
      GConverter.DeleteTempFiles;
      //JclDebugExt.ConvertMapFileToJdbgFile('FobisPRS.map');
      // Convert
      GConverter.ConvertToDBG;
      // Exit Program
      Halt(0);
    end;
  end;
end.
