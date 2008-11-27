object Form1: TForm1
  Left = 155
  Top = 187
  Caption = 'Form1'
  ClientHeight = 362
  ClientWidth = 581
  Color = clBtnFace
  Font.Charset = DEFAULT_CHARSET
  Font.Color = clWindowText
  Font.Height = -10
  Font.Name = 'MS Sans Serif'
  Font.Style = []
  OldCreateOrder = False
  PixelsPerInch = 96
  TextHeight = 13
  object Memo1: TMemo
    Left = 0
    Top = 0
    Width = 581
    Height = 328
    Align = alClient
    ScrollBars = ssVertical
    TabOrder = 0
  end
  object Panel1: TPanel
    Left = 0
    Top = 328
    Width = 581
    Height = 34
    Align = alBottom
    BevelOuter = bvNone
    TabOrder = 1
    object bCallstack: TButton
      Left = 0
      Top = 6
      Width = 117
      Height = 20
      Caption = 'Generate Callstack'
      TabOrder = 0
      OnClick = bCallstackClick
    end
  end
end
