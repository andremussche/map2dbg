using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace tds2pdbproto
{
	struct NumericLeaf
	{
		private ushort m_idOrVal;
		private object m_value;

		public static NumericLeaf Read(BinaryReader rdr)
		{
			NumericLeaf result = new NumericLeaf();
			result.m_idOrVal = rdr.ReadUInt16();
			if (result.m_idOrVal < 0x8000)
			{
				result.m_value = result.m_idOrVal;
			}
			else
			{
				switch (result.m_idOrVal)
				{
					case 0x8000:
						result.m_value = rdr.ReadSByte();
						break;
					case 0x8001:
						result.m_value = rdr.ReadInt16();
						break;
					case 0x8002:
						result.m_value = rdr.ReadUInt16();
						break;
					case 0x8003:
						result.m_value = rdr.ReadInt32();
						break;
					case 0x8004:
						result.m_value = rdr.ReadUInt32();
						break;
					default:
						Debug.Fail("Not implemented NumericLeaf");
						break;
				}
			}
			return result;
		}

		public object GetValue()
		{
			return m_value;
		}

		public byte[] GetBytes()
		{
			if (m_idOrVal < 0x8000)
			{
				return BitConverter.GetBytes(m_idOrVal);
			}
			else
			{
				List<byte> result = new List<byte>();
				result.AddRange(BitConverter.GetBytes(m_idOrVal));
				switch (m_idOrVal)
				{
					case 0x8000:
						result.AddRange(BitConverter.GetBytes((sbyte)m_value));
						break;
					case 0x8001:
						result.AddRange(BitConverter.GetBytes((short)m_value));
						break;
					case 0x8002:
						result.AddRange(BitConverter.GetBytes((ushort)m_value));
						break;
					case 0x8003:
						result.AddRange(BitConverter.GetBytes((int)m_value));
						break;
					case 0x8004:
						result.AddRange(BitConverter.GetBytes((uint)m_value));
						break;
					default:
						Debug.Fail("Not implemented NumericLeaf");
						return null;
				}
				return result.ToArray();
			}
		}

		internal void Assign(ushort val)
		{
			if (val < 0x8000)
			{
				m_idOrVal = val;
			}
			else
			{
				m_idOrVal = 0x8002;
				m_value = val;
			}
		}
	}

	enum MPropType
	{
		None = 0,
		Virtual = 1,
		Static = 2,
		Friend = 3,
		IntroducingVirtual = 4,
		PureVirtual = 5,
		PureIntroducingVirtual = 6,
	}

	enum AccessType
	{
		NoProtection = 0,
		Private = 1,
		Protected = 2,
		Public = 3,
	}

	[Flags]
	enum TdsMemberAttributes
	{
		NeverInstantuated = 1,
		NoInherit = 2,
		NoConstruct = 4,
		Operator = 8,
		Unknown1 = 0x10,
		Constructor = 0x20,
		Unknown2 = 0x40
        
        //argument out of range?
		//AutoMethod = 0x80, // метод добавлен автоматически
		//Unknown3 = 0x100,
	}

	struct TdsMemberAttribute
	{
		public AccessType Access;
		public MPropType MProp;
		public TdsMemberAttributes Attributes;

		public static TdsMemberAttribute Read(BinaryReader rdr)
		{
			int val = rdr.ReadUInt16();
			TdsMemberAttribute result = new TdsMemberAttribute();
			result.Access = (AccessType)(val & 3);
			int mprop = (val >> 2) & 7;
			if (mprop == 7)
				Debug.Fail("MemberAttribute parse error, reserved mprop value");
			result.MProp = (MPropType)mprop;
			result.Attributes = (TdsMemberAttributes)((val >> 5) & 0x1ff);
			if ((val & 0xc000) != 0)
				Debug.Fail("MemberAttribute parse error, reserved flags");
			return result;
		}
	}

	enum SubSectType
	{
		Module = 0x120,
		AlignSym = 0x125,
		SrcModule = 0x127,
		GlobalSym = 0x129,
		GlobalTypes = 0x12b,
		Names = 0x130,
	}

	struct SubSectInfo
	{
		public SubSectType SubSectType;
		public short ModuleIndex;
		public int Offset;
		public int Size;
	}

	struct SegInfo
	{
		public short SegIndex;
		public short Flags;
		public int Offset;
		public int Length;
	}

	class ModuleInfo
	{
		public short OverlayNumber;
		public short LibraryIndex;
		public string Style;
		public SegInfo[] CodeSegs;
		public string Name;
		public SourcesInfo Sources;
		public List<SymbolInfo> Symbols = new List<SymbolInfo>();
	}

	struct SourceLineInfo
	{
		public short LineNo;
		public int Offset;
	}

	struct SourceFileRangeInfo
	{
		public short SegIndex;
		public int StartOffset;
		public int EndOffset;
		public SourceLineInfo[] Lines;
	}

	class SourceFileInfo
	{
		public string Name;
		public SourceFileRangeInfo[] Ranges;
	}

	struct SourcesRangeInfo
	{
		public short SegIndex;
		public int StartOffset;
		public int EndOffset;
	}

	class SourcesInfo
	{
		public SourcesRangeInfo[] Ranges;
		public List<SourceFileInfo> SourceFiles = new List<SourceFileInfo>();
	}

	enum TypeKind
	{
		Primitive = -1,
		Modifier = 1,
		Pointer = 2,
		Array = 3,
		Class = 4,
		Struct = 5,
		Union = 6,
		Enum = 7,
		Procedure = 8,
		MFunction = 9, // member function
		VtabShape = 0xA, // virtual function table shape
		Label = 0xE,
		PasSet = 0x30, // pascal set type
		PasSubrange = 0x31, // pascal subrange type
		PasPArray = 0x32,
		PasPString = 0x33,
		PasClosure = 0x34, // pascal closure type
		PasProperty = 0x35, // pascal property
		PasLString = 0x36,
		PasVariant = 0x37,
		PasClassRef = 0x38,
		PasUnknown39 = 0x39,
		UnknownEF = 0xef,
		ArgList = 0x201,
		FieldList = 0x204,
		BitField = 0x206,
		MList = 0x207,
		RealBaseClass = 0x400,
		DirectVirtBaseClass = 0x401,
		IndirectVirtBaseClass = 0x402,
		Enumerate = 0x403,
		Index = 0x405,
		DataMember = 0x406,
		StaticDataMember = 0x407,
		MethodMember = 0x408,
		NestedType = 0x409,
		VtabPointer = 0x40A, // virtual function table pointer
	}

	enum PrimitiveType
	{
		Void = 3,
		Int32 = 0x74,
		Int64 = 0x76,
	}

	interface ITypeInfoVisitor
	{
		void Visit(PrimitiveTypeInfo primType);
		void Visit(TypModifier modifierType);
		void Visit(TypPointer pointerType);
		void Visit(TypArray arrayType);
		void Visit(TypStruct structType);
		void Visit(TypUnion unionType);
		void Visit(TypEnum enumType);
		void Visit(TypProcedure procType);
		void Visit(TypMFunction mfuncType);
		void Visit(TypVtabShape vtabShape);
		void Visit(TypLabel label);
		void Visit(TypPasSet set);
		void Visit(TypPasSubRange subrange);
		void Visit(TypPasPArray parray);
		void Visit(TypPasPString pstring);
		void Visit(TypPasClosure closure);
		void Visit(TypPasProperty prop);
		void Visit(TypPasLString lstring);
		void Visit(TypPasVariant variant);
		void Visit(TypPasClassRef classref);
		void Visit(TypPasUnknown39 unknown39);
		void Visit(TypArgList argsType);
		void Visit(TypMembersList fieldsType);
		void Visit(TypBitField bitField);
		void Visit(TypMList mlistType);
	}

	abstract class TypeInfo
	{
		public object Tag;
		public abstract void Visit(ITypeInfoVisitor visitor);
	}

	abstract class BaseTypeRef
	{
		private TypeInfo m_owner;
		public int Index;
		public abstract TypeInfo Reference { get; set; }
		protected BaseTypeRef(TypeInfo owner)
		{
			m_owner = owner;
		}
	}

	class TypeRef<T> : BaseTypeRef where T : TypeInfo
	{
		public T Ref;

		public override TypeInfo Reference { get { return Ref; } set { Ref = (T)value; } }
		public TypeRef(TypeInfo owner) : base(owner) {}
	}


	class PrimitiveTypeInfo : TypeInfo
	{
		private PrimitiveType _type;

		internal PrimitiveTypeInfo(PrimitiveType type)
		{
			_type = type;
		}

		public PrimitiveType PrimitiveType { get { return _type; } }

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	[Flags]
	public enum ModifierAttributes
	{
		Const = 1,
		Volatile = 2,
		Unaligned = 4,
	}

	class TypModifier : TypeInfo
	{
		public ModifierAttributes Attribute;
		public TypeRef<TypeInfo> Type;

		public TypModifier()
		{
			Type = new TypeRef<TypeInfo>(this);
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}

		internal static TypeInfo Read(BinaryReader rdr)
		{
			TypModifier result = new TypModifier();
			int modifierAttribs = rdr.ReadInt16();
			if ((modifierAttribs & 0xfff8) != 0)
				Debug.Fail("Unimplemented flags in modifier attribute: " + modifierAttribs.ToString());
			result.Attribute = (ModifierAttributes)modifierAttribs;
			Program.ReadTypeRef(result.Type, rdr);
			return result;
		}
	}

	enum PointerType
	{
		Near = 0,
		Far = 1,
		Huge = 2,
		BasedOnSeg = 3,
		BasedOnVal = 4,
		BasedOnSegOfVal = 5,
		BasedOnAddrOfSym = 6,
		BasedOnSegOfSym = 7,
		BasedOnType = 8,
		BasedOnSelf = 9,
		Near32 = 10,
		Far32 = 11,
	}

	enum PointerMode
	{
		Pointer = 0,
		Reference = 1,
		PtrToDataMember = 2,
		PtrToMethod = 3,
	}

	[Flags]
	enum PointerAttributes
	{
		IsFlat32 = 1,
		Const = 2,
		Volatile = 4,
		Unaligned = 8,
	}

	struct PointerAttribute
	{
		public PointerType PtrType;
		public PointerMode Mode;
		public PointerAttributes Attributes;

		public static PointerAttribute ReadFromCv(BinaryReader rdr)
		{
			int val = rdr.ReadUInt16();
			PointerAttribute result = new PointerAttribute();
			int ptrType = val & 0x1f;
			if (ptrType > 11)
				Debug.Fail("Unknown pointer type: " + ptrType.ToString());
			result.PtrType = (PointerType)ptrType;
			int mode = (val >> 5) & 7;
			if (mode > 3)
				Debug.Fail("Unknown pointer mode: " + mode.ToString());
			result.Mode = (PointerMode)mode;
			int flags = (val >> 8) & 0xff;
			if ((flags & 0xf0) != 0)
				Debug.Fail("Unknown pointer flags: " + (flags & 0xf0).ToString());
			result.Attributes = (PointerAttributes)flags;
			return result;
		}

		public byte[] GetCvBytes()
		{
			ushort result = (ushort)(((int)PtrType) | (((int)Mode) << 5) | (((int)Attributes) << 8));
			return BitConverter.GetBytes(result);
		}
	}

	class TypPointer : TypeInfo
	{
		public PointerAttribute Attribute;
		public TypeRef<TypeInfo> PointedType;
		public TypeRef<TypeInfo> ClassType; // for pointer to data member/method
		public short PtrToDataMembAttrib; // for pointer to data member/method

		public TypPointer()
		{
			PointedType = new TypeRef<TypeInfo>(this);
			ClassType = new TypeRef<TypeInfo>(this);
		}

		public static TypPointer Read(BinaryReader rdr)
		{
			TypPointer result = new TypPointer();
			result.Attribute = PointerAttribute.ReadFromCv(rdr);
			Program.ReadTypeRef(result.PointedType, rdr);
			switch (result.Attribute.PtrType)
			{
				case PointerType.Near:
				case PointerType.Near32:
					break;
				default:
					Debug.Fail("Not implemented pointer type: " + result.Attribute.PtrType.ToString());
					break;
			}
			switch (result.Attribute.Mode)
			{
				case PointerMode.Pointer:
				case PointerMode.Reference:
					break;
				case PointerMode.PtrToDataMember:
					result.PtrToDataMembAttrib = rdr.ReadInt16();
					Program.ReadTypeRef(result.ClassType, rdr);
					break;
				case PointerMode.PtrToMethod:
					if (result.Attribute.PtrType == PointerType.Near)
						result.Attribute.PtrType = PointerType.Near32;
					result.PtrToDataMembAttrib = rdr.ReadInt16();
					Program.ReadTypeRef(result.ClassType, rdr);
					break;
				default:
					Debug.Fail("Not implemented pointer mode: " + result.Attribute.Mode.ToString());
					break;
			}
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypArray : TypeInfo
	{
		public TypeRef<TypeInfo> ElementType;
		public TypeRef<TypeInfo> IndexType;
		public NumericLeaf Length;
		public string Name;

		public TypArray()
		{
			ElementType = new TypeRef<TypeInfo>(this);
			IndexType = new TypeRef<TypeInfo>(this);
		}

		public static TypArray Read(BinaryReader rdr)
		{
			TypArray result = new TypArray();
			Program.ReadTypeRef(result.ElementType, rdr);
			Program.ReadTypeRef(result.IndexType, rdr);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.Length = NumericLeaf.Read(rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	[Flags]
	enum StructFlags
	{
		Packed = 1,
		CtorDtor = 2, // contains constructors and/or destructor
		OverOpers = 4, // contains overloaded operators
		IsNested = 8,
		CNested = 0x10, // contains nested classes
		OpAssign = 0x20, // has overloaded assignment
		OpCast = 0x40, // has casting methods
		FwdRef = 0x80, // is forward reference (incomplete)
		Scoped = 0x100, // this is a scoped definition
	}

	[Flags]
	enum TdsStructFlags
	{
		Packed = 1,
		Ctor = 2, // contains constructors
		OverOpers = 4, // contains overloaded operators
		IsNested = 8,
		CNested = 0x10, // contains nested classes
		OpAssign = 0x20, // has overloaded assignment
		OpCast = 0x40, // has casting methods
		FwdRef = 0x80, // is forward reference (incomplete)
		Dtor = 0x100, // contains destructor
	}

	class TypStruct : TypeInfo
	{
		public TypeRef<TypMembersList> Members;
		public TdsStructFlags Flags;
		public TypeRef<TypeInfo> ContainingType;
		public TypeRef<TypeInfo> DerivationList;
		public TypeRef<TypeInfo> VTable;
		public string Name;
		public NumericLeaf Size;
		public bool IsClass;

		public TypStruct()
		{
			Members = new TypeRef<TypMembersList>(this);
			ContainingType = new TypeRef<TypeInfo>(this);
			DerivationList = new TypeRef<TypeInfo>(this);
			VTable = new TypeRef<TypeInfo>(this);
		}

		public static TypStruct Read(bool isClass, BinaryReader rdr)
		{
			TypStruct result = new TypStruct();
			result.IsClass = isClass;
			ushort numMembers = rdr.ReadUInt16();
			Program.ReadTypeRef(result.Members, rdr);
			result.Flags = Program.ReadStructAttribs(rdr);
			Program.ReadTypeRef(result.ContainingType, rdr);
			Program.ReadTypeRef(result.DerivationList, rdr);
			Program.ReadTypeRef(result.VTable, rdr);
			int strucNameId = rdr.ReadInt32();
			result.Name = Program.NameFromId(strucNameId);
			if (result.Name != null)
				result.Name = NameTranslator.TranslateUdtName(result.Name);
			result.Size = NumericLeaf.Read(rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypUnion : TypeInfo
	{
		public TypeRef<TypMembersList> Members;
		public TdsStructFlags Flags;
		public TypeRef<TypeInfo> ContainingType;
		public string Name;
		public NumericLeaf Size;

		public TypUnion()
		{
			Members = new TypeRef<TypMembersList>(this);
			ContainingType = new TypeRef<TypeInfo>(this);
		}

		public static TypUnion Read(BinaryReader rdr)
		{
			TypUnion result = new TypUnion();
			ushort numMembers = rdr.ReadUInt16();
			Program.ReadTypeRef(result.Members, rdr);
			result.Flags = Program.ReadStructAttribs(rdr);
			Program.ReadTypeRef(result.ContainingType, rdr);
			int unionNameId = rdr.ReadInt32();
			result.Name = Program.NameFromId(unionNameId);
			if (result.Name != null)
				result.Name = NameTranslator.TranslateUdtName(result.Name);
			result.Size = NumericLeaf.Read(rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypEnum : TypeInfo
	{
		public ushort Count;
		public TypeRef<TypeInfo> Type;
		public TypeRef<TypMembersList> Values;
		public int Class;
		public string Name;

		public TypEnum()
		{
			Type = new TypeRef<TypeInfo>(this);
			Values = new TypeRef<TypMembersList>(this);
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	enum CallConvType
	{
		NearC = 0,
		FarC = 1,
		NearPascal = 2,
		FarPascal = 3,
		NearFastcall = 4,
		FarFastcall = 5,
		NearStdcall = 7,
		FarStdcall = 8,
		NearSyscall = 9,
		FarSyscall = 10,
		ThisCall = 11,
		MipsCall = 12,
		Generic = 13,
	}

	[Flags]
	enum TdsCallConvFlags
	{
		VariableArgs = 1,
	}

	struct TdsCallConv
	{
		public CallConvType Kind;
		public TdsCallConvFlags Flags;

		public static TdsCallConv Read(BinaryReader rdr)
		{
			TdsCallConv result = new TdsCallConv();
			int val = rdr.ReadByte();
			int kind = val & 0x3f;
			if (kind == 6 || kind > 13)
				throw new ApplicationException("Invalid calling convention: " + kind);
			result.Kind = (CallConvType)kind;
			int flags = (val >> 6) & 3;
			if ((flags & ~(int)(TdsCallConvFlags.VariableArgs)) != 0)
				throw new ApplicationException("Invalid flags convention: " + flags);
			result.Flags = (TdsCallConvFlags)flags;
			return result;
		}
	}

	class TypProcedure : TypeInfo
	{
		public TypeRef<TypeInfo> RetType;
		public TdsCallConv CallConv;
		public TypeRef<TypArgList> ArgsRef;

		public TypProcedure()
		{
			RetType = new TypeRef<TypeInfo>(this);
			ArgsRef = new TypeRef<TypArgList>(this);
		}

		public static TypProcedure Read(BinaryReader rdr)
		{
			TypProcedure result = new TypProcedure();
			Program.ReadTypeRef(result.RetType, rdr);
			result.CallConv = TdsCallConv.Read(rdr);
			byte reserved = rdr.ReadByte();
			/*if (reserved != 0)
				throw new ApplicationException("Reserved field not zero in Procedure");*/
			/*short numArgs =*/ rdr.ReadInt16();
			Program.ReadTypeRef(result.ArgsRef, rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypMFunction : TypeInfo
	{
		public TypeRef<TypeInfo> RetType;
		public TypeRef<TypeInfo> ClassType;
		public TypeRef<TypeInfo> ThisType;
		public TdsCallConv CallConv;
		public short NumArgs;
		public TypeRef<TypArgList> Args;
		public int ThisAdjust;

		public TypMFunction()
		{
			RetType = new TypeRef<TypeInfo>(this);
			ClassType = new TypeRef<TypeInfo>(this);
			ThisType = new TypeRef<TypeInfo>(this);
			Args = new TypeRef<TypArgList>(this);
		}

		public static TypMFunction Read(BinaryReader rdr)
		{
			TypMFunction result = new TypMFunction();
			Program.ReadTypeRef(result.RetType, rdr);
			Program.ReadTypeRef(result.ClassType, rdr);
			Program.ReadTypeRef(result.ThisType, rdr);
			result.CallConv = TdsCallConv.Read(rdr);
			byte reserved = rdr.ReadByte();
			/*if (reserved != 0)
				throw new ApplicationException("Reserved field not zero in MFunction");*/
			result.NumArgs = rdr.ReadInt16();
			Program.ReadTypeRef(result.Args, rdr);
			result.ThisAdjust = rdr.ReadInt32();
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	enum VtabShapeFuncType
	{
		Near = 0,
		Far = 1,
		Thin = 2,
		ApDisplacement = 3,
		MetaClassDescriptor = 4,
		Near32 = 5,
		Far32 = 6,
	}

	class TypVtabShape : TypeInfo
	{
		public List<VtabShapeFuncType> Descriptors = new List<VtabShapeFuncType>();

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	enum LabelMode
	{
		Near = 0,
		Far = 4,
	}

	class TypLabel : TypeInfo
	{
		public LabelMode Mode;

		public static TypLabel Read(BinaryReader rdr)
		{
			TypLabel result = new TypLabel();
			result.Mode = (LabelMode)rdr.ReadInt16();
			if (result.Mode != LabelMode.Near && result.Mode != LabelMode.Far)
				throw new ApplicationException("Unknown label mode: " + result.Mode.ToString());
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasSet : TypeInfo
	{
		public TypeRef<TypeInfo> BaseType;
		public string Name;
		public NumericLeaf LowByte;
		public NumericLeaf Size;

		public TypPasSet()
		{
			BaseType = new TypeRef<TypeInfo>(this);
		}

		public static TypPasSet Read(BinaryReader rdr)
		{
			TypPasSet result = new TypPasSet();
			Program.ReadTypeRef(result.BaseType, rdr);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.LowByte = NumericLeaf.Read(rdr);
			result.Size = NumericLeaf.Read(rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasSubRange : TypeInfo
	{
		public TypeRef<TypeInfo> BaseType;
		public string Name;
		public NumericLeaf Low;
		public NumericLeaf High;
		public NumericLeaf Size;

		public TypPasSubRange()
		{
			BaseType = new TypeRef<TypeInfo>(this);
		}

		public static TypPasSubRange Read(BinaryReader rdr)
		{
			TypPasSubRange result = new TypPasSubRange();
			Program.ReadTypeRef(result.BaseType, rdr);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.Low = NumericLeaf.Read(rdr);
			result.High = NumericLeaf.Read(rdr);
			result.Size = NumericLeaf.Read(rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasPArray : TypeInfo
	{
		public TypeRef<TypeInfo> ElementType;
		public TypeRef<TypeInfo> IndexType;
		public string Name;
		public NumericLeaf Size;
		public NumericLeaf Elements;

		public TypPasPArray()
		{
			ElementType = new TypeRef<TypeInfo>(this);
			IndexType = new TypeRef<TypeInfo>(this);
		}

		public static TypPasPArray Read(BinaryReader rdr)
		{
			TypPasPArray result = new TypPasPArray();
			Program.ReadTypeRef(result.ElementType, rdr);
			Program.ReadTypeRef(result.IndexType, rdr);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.Size = NumericLeaf.Read(rdr);
			result.Elements = NumericLeaf.Read(rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasPString : TypeInfo
	{
		public TypeRef<TypeInfo> ElementType;
		public TypeRef<TypeInfo> IndexType;
		public string Name;
		public short Unknown1;
		public short Unknown2;

		public TypPasPString()
		{
			ElementType = new TypeRef<TypeInfo>(this);
			IndexType = new TypeRef<TypeInfo>(this);
		}

		public static TypPasPString Read(BinaryReader rdr)
		{
			TypPasPString result = new TypPasPString();
			Program.ReadTypeRef(result.ElementType, rdr);
			Program.ReadTypeRef(result.IndexType, rdr);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.Unknown1 = rdr.ReadInt16();
			result.Unknown2 = rdr.ReadInt16();
			/*if (result.Unknown1 != 0x20 && result.Unknown1 != 0x40 ||
				result.Unknown2 != 0x20 && result.Unknown2 != 0x40)
			{
				throw new ApplicationException("Unknown value of unknown1 or unknown2 in PSTRING");
			}*/
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasClosure : TypeInfo
	{
		public TypeRef<TypeInfo> RetType;
		public TdsCallConv CallConv;
		public TypeRef<TypArgList> ArgsRef;
		public TypStruct Replacement = null;

		public TypPasClosure()
		{
			RetType = new TypeRef<TypeInfo>(this);
			ArgsRef = new TypeRef<TypArgList>(this);
		}

		public static TypPasClosure Read(BinaryReader rdr)
		{
			TypPasClosure result = new TypPasClosure();
			Program.ReadTypeRef(result.RetType, rdr);
			result.CallConv = TdsCallConv.Read(rdr);
			byte reserved = rdr.ReadByte();
            if (reserved != 0)
				//am throw new ApplicationException("Non zero reserved field in PasClosure");*/
			/*short numArgs =*/ rdr.ReadInt16();
			Program.ReadTypeRef(result.ArgsRef, rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	[Flags]
	enum PasPropertyFlags
	{
		Default = 1,
		HasReader = 2,
		HasWriter = 4,
	}

	class TypPasProperty : TypeInfo
	{
		public TypeRef<TypeInfo> Type;
		public PasPropertyFlags Flags;
		public TypeRef<TypeInfo> IndexType;
		public int PropertyIndex;
		public string Reader;
		public string Writer;
		public int ReadSlot;
		public int WriteSlot;

		public TypPasProperty()
		{
			Type = new TypeRef<TypeInfo>(this);
			IndexType = new TypeRef<TypeInfo>(this);
		}

		public static TypPasProperty Read(BinaryReader rdr)
		{
			TypPasProperty result = new TypPasProperty();
			Program.ReadTypeRef(result.Type, rdr);
			int flags = rdr.ReadUInt16();
			if ((flags & (int)~(PasPropertyFlags.HasReader | PasPropertyFlags.HasWriter | PasPropertyFlags.Default)) != 0)
				Debug.Fail("Not implemented flags in Property flags field");
			result.Flags = (PasPropertyFlags)flags;
			Program.ReadTypeRef(result.IndexType, rdr);
			result.PropertyIndex = rdr.ReadInt32();
			int reader = rdr.ReadInt32();
			int writer = rdr.ReadInt32();
			if ((result.Flags & PasPropertyFlags.HasReader) == 0)
				result.Reader = Program.NameFromId(reader);
			else
				result.ReadSlot = reader;
			if ((result.Flags & PasPropertyFlags.HasWriter) == 0)
				result.Writer = Program.NameFromId(writer);
			else
				result.WriteSlot = writer;
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasLString : TypeInfo
	{
		public string Name;

		public static TypPasLString Read(BinaryReader rdr)
		{
			TypPasLString result = new TypPasLString();
			result.Name = Program.NameFromId(rdr.ReadInt32());
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasVariant : TypeInfo
	{
		public string Name;

		public static TypPasVariant Read(BinaryReader rdr)
		{
			TypPasVariant result = new TypPasVariant();
			result.Name = Program.NameFromId(rdr.ReadInt32());
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasClassRef : TypeInfo
	{
		public TypeRef<TypeInfo> BaseType;
		public TypeRef<TypVtabShape> VtabShape;

		public TypPasClassRef()
		{
			BaseType = new TypeRef<TypeInfo>(this);
			VtabShape = new TypeRef<TypVtabShape>(this);
		}

		public static TypPasClassRef Read(BinaryReader rdr)
		{
			TypPasClassRef result = new TypPasClassRef();
			Program.ReadTypeRef(result.BaseType, rdr);
			Program.ReadTypeRef(result.VtabShape, rdr);
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypPasUnknown39 : TypeInfo
	{
		public int Unknown;

		public static TypPasUnknown39 Read(BinaryReader rdr)
		{
			TypPasUnknown39 result = new TypPasUnknown39();
			result.Unknown = rdr.ReadInt32();
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypArgList : TypeInfo
	{
		public TypeRef<TypeInfo>[] Args;

		public static TypArgList Read(BinaryReader rdr)
		{
			short numArgs = rdr.ReadInt16();
			TypArgList result = new TypArgList();
			result.Args = new TypeRef<TypeInfo>[numArgs];
			for (int ai = 0; ai < numArgs; ai++)
			{
				result.Args[ai] = new TypeRef<TypeInfo>(result);
				Program.ReadTypeRef(result.Args[ai], rdr);
			}
			return result;
		}

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypMembersList : TypeInfo
	{
		public List<TypMember> SubFields = new List<TypMember>();

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class TypBitField : TypeInfo
	{
		public byte Length;
		public byte Pos;
		public TypeInfo Type;

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MListItem
	{
		public TdsMemberAttribute Attributes;
		public TypeRef<TypMFunction> Func;
		public int BrowserOffset;
		public int VTabOffset;
	}

	class TypMList : TypeInfo
	{
		public List<MListItem> Methods = new List<MListItem>();

		public override void Visit(ITypeInfoVisitor visitor)
		{
			visitor.Visit(this);
		}

		internal static TypeInfo Read(BinaryReader rdr, int end)
		{
			TypMList result = new TypMList();
			while (rdr.BaseStream.Position < end)
			{
				MListItem item = new MListItem();
				item.Attributes = TdsMemberAttribute.Read(rdr);
				item.Func = new TypeRef<TypMFunction>(result);
				Program.ReadTypeRef(item.Func, rdr);
				item.BrowserOffset = rdr.ReadInt32();
				if (item.Attributes.MProp == MPropType.IntroducingVirtual ||
					item.Attributes.MProp == MPropType.PureIntroducingVirtual)
				{
					item.VTabOffset = rdr.ReadInt32();
				}
				result.Methods.Add(item);
			}
			return result;
		}
	}

	interface ITypMemberVisitor
	{
		void Visit(MemRealBaseClass realbase);
		void Visit(MemVirtualBaseClass virtbase);
		void Visit(MemEnumerate memenum);
		void Visit(MemIndex index);
		void Visit(MemInstanceData instdata);
		void Visit(MemStaticData statdata);
		void Visit(MemMethod method);
		void Visit(MemMethods methods);
		void Visit(MemNestedType nesttype);
		void Visit(MemVtabPointer vtabptr);
	}

	abstract class TypMember
	{
		public abstract void Visit(ITypMemberVisitor visitor);
	}

	class MemRealBaseClass : TypMember
	{
		public TypeRef<TypeInfo> Class;
		public TdsMemberAttribute Attribute;
		public NumericLeaf Offset;

		public MemRealBaseClass()
		{
			Class = new TypeRef<TypeInfo>(null);
		}

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}

		public static MemRealBaseClass Read(BinaryReader rdr)
		{
			MemRealBaseClass result = new MemRealBaseClass();
			Program.ReadTypeRef(result.Class, rdr);
			result.Attribute = TdsMemberAttribute.Read(rdr);
			result.Offset = NumericLeaf.Read(rdr);
			return result;
		}
	}

	class MemVirtualBaseClass : TypMember
	{
		public bool IsDirect;
		public TypeInfo Class;
		public TypeInfo PtrType;
		public TdsMemberAttribute Attribute;
		public NumericLeaf Offset;
		public NumericLeaf VbaseDispIndex;

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MemEnumerate : TypMember
	{
		public short Attribute;
		public string Name;
		public int BrowserOffset;
		public NumericLeaf Value;

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MemIndex : TypMember
	{
		public TypeInfo Continuation;

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MemInstanceData : TypMember
	{
		public TypeRef<TypeInfo> Type;
		public TdsMemberAttribute Attributes;
		public string Name;
		public int BrowserOffset;
		public NumericLeaf Offset;

		public MemInstanceData()
		{
			Type = new TypeRef<TypeInfo>(null);
		}

		internal static TypMember Read(BinaryReader rdr)
		{
			MemInstanceData result = new MemInstanceData();
			Program.ReadTypeRef(result.Type, rdr);
			result.Attributes = TdsMemberAttribute.Read(rdr);
			int dataMemberNameId = rdr.ReadInt32();
			result.Name = Program.NameFromId(dataMemberNameId);
			result.BrowserOffset = rdr.ReadInt32();
			result.Offset = NumericLeaf.Read(rdr);
			return result;
		}

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MemStaticData : TypMember
	{
		public TypeInfo Type;
		public TdsMemberAttribute Attributes;
		public string Name;
		public int BrowserOffset;

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MemMethod : TypMember
	{
		public TypMFunction MethodType;
		public TdsMemberAttribute Attributes;
		public int VTabOffset;
		public string Name;

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MemMethods : TypMember
	{
		public TypeRef<TypMList> List = new TypeRef<TypMList>(null);
		public string Name;

		public static MemMethods Read(BinaryReader rdr)
		{
			MemMethods result = new MemMethods();
			short numMethods = rdr.ReadInt16();
			Program.ReadTypeRef(result.List, rdr);
			//if (result.List.Methods.Count != numMethods)
			//	throw new ApplicationException("Inconsistent number of methods in MemMethods and TypMList");
			result.Name = NameTranslator.TranslateUdtName(Program.NameFromId(rdr.ReadInt32()));
			return result;
		}

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class MemNestedType : TypMember
	{
		public TypeRef<TypeInfo> NestedType;
		public string Name;
		public int BrowserOffset;

		public MemNestedType()
		{
			NestedType = new TypeRef<TypeInfo>(null);
		}

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}

		internal static TypMember Read(BinaryReader rdr)
		{
			MemNestedType result = new MemNestedType();
			Program.ReadTypeRef(result.NestedType, rdr);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.BrowserOffset = rdr.ReadInt32();
			return result;
		}
	}

	class MemVtabPointer : TypMember
	{
		public TypeRef<TypeInfo> VtabShapePtr = new TypeRef<TypeInfo>(null);
		public NumericLeaf Offset;

		public override void Visit(ITypMemberVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	enum SymbolType
	{
		Compile = 1,
		Register = 2,
		Constant = 3,
		Udt = 4,
		SymSearch = 5,
		End = 6,
		GProcRef = 0x20,
		GDataRef = 0x21,
		EData = 0x22,
		EProc = 0x23,
		Uses = 0x24,
		Namespace = 0x25,
		Using = 0x26,
		PConstant = 0x27,
		BpRelative32 = 0x200,
		LData32 = 0x201,
		GData32 = 0x202,
		Pub32 = 0x203,
		LocalProc32 = 0x204,
		GlobalProc32 = 0x205,
		Thunk32 = 0x206,
		Block32 = 0x207,
		With32 = 0x208,
		Label32 = 0x209,
		VftPath32 = 0x20B,
		VftRegRel32 = 0x20C,
		LThread32 = 0x20D,
		GThread32 = 0x20E,
		Entry32 = 0x210,
		OptVar32 = 0x211, // optimized variable
		ProcRet32 = 0x212,
		SaveRegs32 = 0x213,
		SLink32 = 0x230,
	}

	interface ISymbolVisitor
	{
		void Visit(SymCompile comp);
		void Visit(SymRegister reg);
		void Visit(SymConstant symConst);
		void Visit(SymUdt udt);
		void Visit(SymSearch search);
		void Visit(SymEnd end);
		void Visit(SymGProcRef procRef);
		void Visit(SymGDataRef dataRef);
		void Visit(SymBpRelative32Info bprel);
		void Visit(SymData32 symData);
		void Visit(SymProc32Info procInfo);
		void Visit(SymThunk32 thunk32);
		void Visit(SymBlock32 block32);
		void Visit(SymWith32 with32);
		void Visit(SymLabel32 label);
		void Visit(SymEntry32 entry);
		void Visit(SymOptVar32 optvar);
		void Visit(SymProcRet32 symProcRet);
		void Visit(SymSaveRegs32 saveregs);
		void Visit(SymUses symUses);
		void Visit(SymNamespace symNamespace);
		void Visit(SymUsing symUsing);
		void Visit(SymPConstant pconstant);
		void Visit(SymSLink32 pconstant);
	}

	abstract class SymbolInfo
	{
		internal object Tag;

		abstract public void Visit(ISymbolVisitor visitor);
	}

	enum MachineType
	{
		Intel8080 = 0,
		Intel8086 = 1,
		Intel80286 = 2,
		Intel80386 = 3,
		Intel80486 = 4,
		IntelPentium = 5,
	}

	enum LanguageType
	{
		C = 0,
		Cpp = 1,
		Fortran = 2,
		Masm = 3,
	}

	enum FpuPrecision
	{
		Fast = 0,
		Ansi = 1,
	}

	enum FpuType
	{
		Hardware = 0,
		Emulator = 1,
		Altmath = 2,
	}

	enum AmbientType
	{
		Near = 0,
		Far = 1,
		Huge = 2,
	}

	class SymCompile : SymbolInfo
	{
		public MachineType Machine;
		public LanguageType Language;
		public bool WithPCode;
		public FpuPrecision FpuPrecis;
		public FpuType Fpu;
		public AmbientType AmbientData;
		public AmbientType AmbientCode;
		public bool Mode32;
		public bool IsCharSigned;
		public string Compiler;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	enum RegisterType
	{
		al = 1,
		cl = 2,
		dl = 3,
		bl = 4,
		ah = 5,
		ch = 6,
		dh = 7,
		bh = 8,
		ax = 9,
		cx = 10,
		dx = 11,
		bx = 12,
		sp = 13,
		bp = 14,
		si = 15,
		di = 16,
		eax = 17,
		ecx = 18,
		edx = 19,
		ebx = 20,
		esp = 21,
		ebp = 22,
		esi = 23,
		edi = 24,
	}

	class SymRegister : SymbolInfo
	{
		public TypeInfo Type;
		public RegisterType Register;
		public string Name;
		public int BrowserOffset;

		public static SymRegister Read(BinaryReader rdr)
		{
			SymRegister result = new SymRegister();
			result.Type = Program.TypeInfoFromId2(rdr.ReadInt32());
			result.Register = (RegisterType)rdr.ReadInt16();
            if (result.Register < RegisterType.al || result.Register > RegisterType.edi)
                //am throw new ApplicationException("Unknown register: " + result.Register);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.BrowserOffset = rdr.ReadInt32();
			return result;
		}

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymConstant : SymbolInfo
	{
		public TypeInfo DataType;
		public NumericLeaf Value;
		public string Name;
		public int BrowserOffset;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymUdt : SymbolInfo
	{
		public TypeInfo Type;
		public short Flags;
		public string Name;
		public int BrowserOffset;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	};

	class SymSearch : SymbolInfo
	{
		public int Offset;
		public short Segment;
		public short CodeSyms;
		public short DataSyms;
		public int FirstData;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymEnd : SymbolInfo
	{
		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymGProcRef : SymbolInfo
	{
		public TypeInfo ProcType;
		public string Name;
		public int Offset;
		public short Segment;
		public int Unknown1;
		public int Unknown2;
		public int Unknown3;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymGDataRef : SymbolInfo
	{
		public TypeInfo DataType;
		public string Name;
		public int Offset;
		public short Segment;
		public int Unknown1;
		public int Unknown2;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymBpRelative32Info : SymbolInfo
	{
		public int Offset;
		public TypeInfo DataType;
		public string Name;
		public int BrowserOffset;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	};

	class SymData32 : SymbolInfo
	{
		public bool IsGlobal;
		public int Offset;
		public short Segment;
		public short Flags;
		public TypeInfo DataType;
		public string Name;
		public int BrowserOffset;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class NestingOffsets
	{
		public int Parent;
		public int End;
		public int Next;
		public bool HasNext;

		public static NestingOffsets ReadFromTds(BinaryReader rdr, bool hasNext)
		{
			NestingOffsets result = new NestingOffsets();
			result.Parent = rdr.ReadInt32();
			result.End = rdr.ReadInt32();
			result.HasNext = hasNext;
			if (hasNext)
				result.Next = rdr.ReadInt32();
			return result;
		}
	}

	abstract class SymScopedInfo : SymbolInfo
	{
		public NestingOffsets NestOffsets;
		public SymbolInfo Parent;
		public SymbolInfo End;
		public SymbolInfo Next;
	}

	class SymProc32Info : SymScopedInfo
	{
		public bool IsGlobal;
		public int Size;
		public int DebugStart;
		public int DebugEnd;
		public int Offset;
		public short Segment;
		public string Name;
		public TypeInfo ProcType;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	enum ThunkType
	{
		NoType = 0,
		Adjustor = 1,
		VirtCall = 2,
		PCode = 3,
	}

	class SymThunk32 : SymScopedInfo
	{
		public int Offset;
		public short Segment;
		public short Length; // length in bytes of this thunk
		public ThunkType Kind;
		public string Name;
		public int Adjust; // only for adjustor thunks, value added to this pointer
		public string FunctionName; // only for adjustor thunks, name of target function
		//public short VtabDisplacement; // only for virtcall thunks, displacement into virtual function table
		//public short PCodeSegment; // only for pcode thunks, segment:offset of pcode entry point
		//public int PCodeOffset; // ...

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymBlock32 : SymScopedInfo
	{
		public int Length; // length in bytes of scope of this block
		public int Offset;
		public short Segment;
		public string Name;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}

		internal static SymbolInfo Read(BinaryReader rdr)
		{
			SymBlock32 result = new SymBlock32();
			result.NestOffsets = NestingOffsets.ReadFromTds(rdr, false);
			result.Length = rdr.ReadInt32();
			result.Offset = rdr.ReadInt32();
			result.Segment = rdr.ReadInt16();
			result.Name = Program.NameFromId(rdr.ReadInt32());
			return result;
		}
	}

	class SymRef
	{
		public int Offset;
		//public SymbolInfo Ref;
	}

	class SymWith32 : SymbolInfo
	{
		public SymRef Parent;
		public int Length; // length in bytes of scope of this block
		public int Offset;
		public short Segment;
		public short Flags;
		public int Type;
		public string Name;
		public int VarOffset;

		public SymWith32()
		{
			Parent = new SymRef();
		}

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}

		internal static SymWith32 Read(BinaryReader rdr)
		{
			SymWith32 result = new SymWith32();
			result.Parent.Offset = rdr.ReadInt32();
			result.Length = rdr.ReadInt32();
			result.Offset = rdr.ReadInt32();
			result.Segment = rdr.ReadInt16();
			result.Flags = rdr.ReadInt16();
			result.Type = rdr.ReadInt32();
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.VarOffset = rdr.ReadInt32();
			return result;
		}
	}

	[Flags]
	enum LabelFlags
	{
		Fpo = 1,
		Interrupt = 2,
		Return = 4,
		Never = 8,
	}

	class SymLabel32 : SymbolInfo
	{
		public int Offset;
		public short Segment;
		public LabelFlags Flags;
		public string Name;
		public int Unknown;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}

		internal static SymLabel32 Read(BinaryReader rdr)
		{
			SymLabel32 result = new SymLabel32();
			result.Offset = rdr.ReadInt32();
			result.Segment = rdr.ReadInt16();
			result.Flags = (LabelFlags)rdr.ReadByte();
			if (((int)result.Flags & 0xf0) != 0)
				throw new ApplicationException("Unknown label flags: " + result.Flags);
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.Unknown = rdr.ReadInt32();
			return result;
		}
	}

	class SymEntry32 : SymbolInfo
	{
		public int Offset;
		public short Segment;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymOptVar32 : SymbolInfo
	{
		public short Unknown;
		public int Start;
		public int Length;
		public short Register;

		public static SymOptVar32 Read(BinaryReader rdr)
		{
			SymOptVar32 result = new SymOptVar32();
			result.Unknown = rdr.ReadInt16();
			result.Start = rdr.ReadInt32();
			result.Length = rdr.ReadInt32();
			result.Register = rdr.ReadInt16();
			return result;
		}

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymProcRet32 : SymbolInfo
	{
		public int Offset;
		public short Length;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymSaveRegs32 : SymbolInfo
	{
		public short Flags;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymUses : SymbolInfo
	{
		public string[] Units;

		public static SymUses Read(int size, BinaryReader rdr)
		{
			SymUses result = new SymUses();
			int numUnits = (size - 2) / 4;
			result.Units = new string[numUnits];
			for (int i = 0; i < numUnits; i++)
				result.Units[i] = Program.NameFromId(rdr.ReadInt32());
			return result;
		}

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymNamespace : SymbolInfo
	{
		public string Name;
		public int BrowserOffset;
		public short UsingCount;

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymUsing : SymbolInfo
	{
		public List<string> Names = new List<string>();

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymPConstant : SymbolInfo
	{
		public TypeInfo DataType;
		public short Property;
		public string Name;
		public int BrowserOffset;
		public int Value;

		public static SymPConstant Read(BinaryReader rdr)
		{
			SymPConstant result = new SymPConstant();
			result.DataType = Program.TypeInfoFromId(rdr.ReadInt32());
			result.Property = rdr.ReadInt16();
			result.Name = Program.NameFromId(rdr.ReadInt32());
			result.BrowserOffset = rdr.ReadInt32();
			result.Value = rdr.ReadInt32();
			return result;
		}

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class SymSLink32 : SymbolInfo
	{
		public int Offset;

		public static SymSLink32 Read(BinaryReader rdr)
		{
			SymSLink32 result = new SymSLink32();
			result.Offset = rdr.ReadInt32();
			return result;
		}

		public override void Visit(ISymbolVisitor visitor)
		{
			visitor.Visit(this);
		}
	}

	class Program
	{
		static TypeInfo[] types;
		static string[] names;
		static ModuleInfo[] modules;
		static List<SymbolInfo> globalSymbols = new List<SymbolInfo>();
		static TypeInfo[] primitives = new TypeInfo[0x1000];

		internal static CallConvType ParseCallConv(int callConv)
		{
			if (callConv == 6 || callConv > 13)
				throw new ApplicationException("Invalid calling convention: " + callConv);
			return (CallConvType)callConv;
		}

		private static List<BaseTypeRef> m_fixup = new List<BaseTypeRef>();

		private static TypeInfo TypeInfoFromIdInternal(int typeId)
		{
			int typeIndex = typeId - 0x1000;
			if (typeIndex < 0)
			{
				if (primitives[typeId] == null)
				{
					primitives[typeId] = new PrimitiveTypeInfo((PrimitiveType)typeId);
				}
				return primitives[typeId];
			}
			else
			{
				return types[typeIndex];
			}
		}

		internal static TdsStructFlags ReadStructAttribs(BinaryReader rdr)
		{
			int flags = rdr.ReadUInt16();
			if ((flags & 0xfe00) != 0)
				throw new ApplicationException("Invalid structure attribs: " + flags.ToString());
			return (TdsStructFlags)flags;
		}

		internal static void ReadTypeRef(BaseTypeRef typeref, BinaryReader rdr)
		{
			int typeId = rdr.ReadInt32();
			typeref.Index = typeId;
			if (typeId == 0)
			{
				typeref.Reference = null;
				return;
			}
			typeref.Reference = TypeInfoFromIdInternal(typeId);
			if (typeref.Reference == null)
				m_fixup.Add(typeref);
		}

		internal static TypeInfo TypeInfoFromId(int typeId)
		{
			//am if (typeId == 0)
				return null;
			TypeInfo result = TypeInfoFromIdInternal(typeId);
			if (result == null)
				throw new ApplicationException("Not loaded type, use TypeRef<> generic: " + typeId.ToString());
			return result;
		}

		internal static TypeInfo TypeInfoFromId2(int typeId)
		{
			//am if (typeId == 0)
				return null;
			TypeInfo result = TypeInfoFromIdInternal(typeId);
			return result;
		}

		internal static string NameFromId(int nameId)
		{
			if (nameId == 0)
				return null;
			
			string str = names[nameId - 1];
			return str;
		}




		static void Main(string[] args)
		{
			if (args.Length < 1)
			{
				Console.WriteLine("tds2pdbproto tdsfile");
				return;
			}
			string tdsFile = args[0];
			string folder = Path.GetDirectoryName(tdsFile);
			string baseFname = Path.GetFileNameWithoutExtension(tdsFile);
			string pdbName = Path.Combine(folder, baseFname + ".pdb");
			string exeName = Path.Combine(folder, baseFname + ".exe");
            if (!File.Exists(exeName))
            {
                //dll exists?
                exeName = Path.Combine(folder, baseFname + ".dll");
                if (!File.Exists(exeName))
                  //no dll, keep .exe for error
                  Path.Combine(folder, baseFname + ".exe");   
            }

			FileStream stm = File.OpenRead(tdsFile);
			ParseTds(stm);
			PdbWriter.WritePdb(globalSymbols, modules, pdbName, exeName);
		}

		private static void ParseTds(FileStream stm)
		{
			BinaryReader rdr = new BinaryReader(stm, Encoding.Default);
			char[] signature = rdr.ReadChars(4);
			//Debug.Assert(new string(signature) == "FB0A");
			int offset = rdr.ReadInt32();

			stm.Position = offset;
			short dirHeaderSize = rdr.ReadInt16();
			Debug.Assert(dirHeaderSize == 16);
			short dirEntrySize = rdr.ReadInt16();
			Debug.Assert(dirEntrySize == 12);
			int sectNum = rdr.ReadInt32();
			int reserved1 = rdr.ReadInt32();
			Debug.Assert(reserved1 == 0);
			int reserved2 = rdr.ReadInt32();
			Debug.Assert(reserved2 == 0);

			SubSectInfo[] subSectInfos = new SubSectInfo[sectNum];
			for (int i = 0; i < sectNum; i++)
			{
				SubSectInfo info = new SubSectInfo();
				info.SubSectType = (SubSectType)rdr.ReadInt16();
				info.ModuleIndex = rdr.ReadInt16();
				info.Offset = rdr.ReadInt32();
				info.Size = rdr.ReadInt32();
				subSectInfos[i] = info;
			}
			foreach (SubSectInfo info in subSectInfos)
			{
				switch (info.SubSectType)
				{
					case SubSectType.Names:
						stm.Position = info.Offset;
						int numNames = rdr.ReadInt32();
						names = new string[numNames];
						for (int i = 0; i < numNames; i++)
						{
							byte strlen = rdr.ReadByte();
							char[] chars = rdr.ReadChars(strlen);
							rdr.ReadByte(); // skip terminator
							names[i] = new string(chars);
						}
						break;
				}
			}
			int numModules = 0;
			foreach (SubSectInfo info in subSectInfos)
			{
				switch (info.SubSectType)
				{
					case SubSectType.GlobalTypes:
                        break;  //am: skip
						stm.Position = info.Offset;
						int typesUnk1 = rdr.ReadInt32();
						int numTypes = rdr.ReadInt32();
						int[] offsets = new int[numTypes];
						types = new TypeInfo[numTypes];
						for (int i = 0; i < numTypes; i++)
							offsets[i] = rdr.ReadInt32();
						for (int i = 0; i < numTypes; i++)
						{
							int typeStart = info.Offset + offsets[i];
							stm.Position = typeStart;
							short len = rdr.ReadInt16();
							TypeKind type = (TypeKind)rdr.ReadInt16();
							switch (type)
							{
								case TypeKind.Modifier:
									types[i] = TypModifier.Read(rdr);
									break;
								case TypeKind.Pointer:
									types[i] = TypPointer.Read(rdr);
									break;
								case TypeKind.Array:
									types[i] = TypArray.Read(rdr);
									break;
								case TypeKind.Class:
								case TypeKind.Struct:
									types[i] = TypStruct.Read(type == TypeKind.Class, rdr);
									break;
								case TypeKind.Union:
									types[i] = TypUnion.Read(rdr);
									break;
								case TypeKind.Enum:
									TypEnum enumInfo = new TypEnum();
									enumInfo.Count = rdr.ReadUInt16();
									ReadTypeRef(enumInfo.Type, rdr);
									ReadTypeRef(enumInfo.Values, rdr);
									enumInfo.Class = rdr.ReadInt32();
									enumInfo.Name = NameFromId(rdr.ReadInt32());
									if (enumInfo.Name != null)
										enumInfo.Name = NameTranslator.TranslateUdtName(enumInfo.Name);
									types[i] = enumInfo;
									break;
								case TypeKind.Procedure:
									types[i] = TypProcedure.Read(rdr);
									break;
								case TypeKind.MFunction:
									types[i] = TypMFunction.Read(rdr);
									break;
								case TypeKind.VtabShape:
									TypVtabShape vtabShape = new TypVtabShape();
									short vtabDescrNum = rdr.ReadInt16();
									byte vtabDescrByte = 0;
									for (int descri = 0; descri < vtabDescrNum; descri++)
									{
										int vtabDescr;
										if (descri % 2 == 0)
										{
											vtabDescrByte = rdr.ReadByte();
											vtabDescr = vtabDescrByte & 0xF;
										}
										else
										{
											vtabDescr = (vtabDescrByte >> 4) & 0xF;
										}
										if (vtabDescr < 0 || 6 < vtabDescr)
											Debug.Fail("Unknown virtual function table descriptor type: " + vtabDescr.ToString());
										vtabShape.Descriptors.Add((VtabShapeFuncType)vtabDescr);
									}
									types[i] = vtabShape;
									break;
								case TypeKind.Label:
									types[i] = TypLabel.Read(rdr);
									break;
								case TypeKind.PasSet:
									types[i] = TypPasSet.Read(rdr);
									break;
								case TypeKind.PasSubrange:
									types[i] = TypPasSubRange.Read(rdr);
									break;
								case TypeKind.PasPArray:
									types[i] = TypPasPArray.Read(rdr);
									break;
								case TypeKind.PasPString:
									types[i] = TypPasPString.Read(rdr);
									break;
								case TypeKind.PasClosure:
									types[i] = TypPasClosure.Read(rdr);
									break;
								case TypeKind.PasProperty:
									types[i] = TypPasProperty.Read(rdr);
									break;
								case TypeKind.PasLString:
									types[i] = TypPasLString.Read(rdr);
									break;
								case TypeKind.PasVariant:
									types[i] = TypPasVariant.Read(rdr);
									break;
								case TypeKind.PasClassRef:
									types[i] = TypPasClassRef.Read(rdr);
									break;
								case TypeKind.PasUnknown39:
									types[i] = TypPasUnknown39.Read(rdr);
									break;
								case TypeKind.ArgList:
									types[i] = TypArgList.Read(rdr);
									break;
								case TypeKind.FieldList:
									TypMembersList fieldsInfo = new TypMembersList();
									while (stm.Position < typeStart + len)
									{
										TypeKind leaf = (TypeKind)rdr.ReadInt16();
										TypMember subField = null;
										switch (leaf)
										{
											case TypeKind.RealBaseClass:
												subField = MemRealBaseClass.Read(rdr);
												break;
											case TypeKind.DirectVirtBaseClass:
											case TypeKind.IndirectVirtBaseClass:
												MemVirtualBaseClass virtualBaseClass = new MemVirtualBaseClass();
												subField = virtualBaseClass;
												virtualBaseClass.IsDirect = type == TypeKind.DirectVirtBaseClass;
												virtualBaseClass.Class = TypeInfoFromId(rdr.ReadInt32());
												virtualBaseClass.PtrType = TypeInfoFromId(rdr.ReadInt32());
												virtualBaseClass.Attribute = TdsMemberAttribute.Read(rdr);
												virtualBaseClass.Offset = NumericLeaf.Read(rdr);
												virtualBaseClass.VbaseDispIndex = NumericLeaf.Read(rdr);
												break;
											case TypeKind.Enumerate:
												MemEnumerate enumerate = new MemEnumerate();
												subField = enumerate;
												enumerate.Attribute = rdr.ReadInt16();
												int enumerateNameId = rdr.ReadInt32();
												enumerate.Name = NameTranslator.Parse(NameFromId(enumerateNameId)).Tag;
												enumerate.BrowserOffset = rdr.ReadInt32();
												enumerate.Value = NumericLeaf.Read(rdr);
												break;
											case TypeKind.Index:
												MemIndex indexInfo = new MemIndex();
												indexInfo.Continuation = TypeInfoFromId(rdr.ReadInt32());
												subField = indexInfo;
												break;
											case TypeKind.DataMember:
												subField = MemInstanceData.Read(rdr);
												break;
											case TypeKind.StaticDataMember:
												MemStaticData staticDataMember = new MemStaticData();
												subField = staticDataMember;
												staticDataMember.Type = TypeInfoFromId(rdr.ReadInt32());
												staticDataMember.Attributes = TdsMemberAttribute.Read(rdr);
												staticDataMember.Name = NameFromId(rdr.ReadInt32());
												staticDataMember.BrowserOffset = rdr.ReadInt32();
												break;
											case TypeKind.MethodMember:
												subField = MemMethods.Read(rdr);
												break;
											case TypeKind.NestedType:
												subField = MemNestedType.Read(rdr);
												break;
											case TypeKind.VtabPointer:
												MemVtabPointer vtabPointer = new MemVtabPointer();
												Program.ReadTypeRef(vtabPointer.VtabShapePtr, rdr);
												vtabPointer.Offset = NumericLeaf.Read(rdr);
												subField = vtabPointer;
												break;
											default:
												Debug.Fail("Unknown leaf: " + leaf.ToString());
												break;
										}
										fieldsInfo.SubFields.Add(subField);
										byte b = rdr.ReadByte();
										if (b > 0xf0)
											stm.Position += (b & 0xf) - 1;
										else
											stm.Position--;
									}
									types[i] = fieldsInfo;
									break;
								case TypeKind.BitField:
									TypBitField bitField = new TypBitField();
									types[i] = bitField;
									bitField.Length = rdr.ReadByte();
									bitField.Pos = rdr.ReadByte();
									bitField.Type = TypeInfoFromId(rdr.ReadInt32());
									break;
								case TypeKind.MList:
									types[i] = TypMList.Read(rdr, typeStart + len);
									break;
								case TypeKind.UnknownEF:
									break;
								default:
									Debug.Fail("Unknown type id: " + type.ToString());
									break;
							}
						}
						break;
					default:
						if (info.ModuleIndex != 0 && info.ModuleIndex != -1)
							numModules = Math.Max(info.ModuleIndex, numModules);
						break;
				}
			}
			foreach (BaseTypeRef typeref in m_fixup)
				typeref.Reference = TypeInfoFromIdInternal(typeref.Index);          
            modules = new ModuleInfo[numModules];
			foreach (SubSectInfo info in subSectInfos)
			{
				if (info.ModuleIndex == 0 || info.ModuleIndex == -1)
					continue;
				if (modules[info.ModuleIndex - 1] == null)
					modules[info.ModuleIndex - 1] = new ModuleInfo();
				ModuleInfo currentModule = modules[info.ModuleIndex - 1];
				stm.Position = info.Offset;
				switch (info.SubSectType)
				{
					case SubSectType.Module:
						currentModule.OverlayNumber = rdr.ReadInt16();
						currentModule.LibraryIndex = rdr.ReadInt16();
						short numCodeSegs = rdr.ReadInt16();
						currentModule.Style = new string(rdr.ReadChars(2));
						int modNameId = rdr.ReadInt32();
						currentModule.Name = NameFromId(modNameId);
						int modUnk1 = rdr.ReadInt32();
						int modUnk2 = rdr.ReadInt32();
						int modUnk3 = rdr.ReadInt32();
						int modUnk4 = rdr.ReadInt32();
						currentModule.CodeSegs = new SegInfo[numCodeSegs];
						for (int i = 0; i < numCodeSegs; i++)
						{
							currentModule.CodeSegs[i].SegIndex = rdr.ReadInt16();
							currentModule.CodeSegs[i].Flags = rdr.ReadInt16();
							currentModule.CodeSegs[i].Offset = rdr.ReadInt32();
							currentModule.CodeSegs[i].Length = rdr.ReadInt32();
						}
						break;
					case SubSectType.SrcModule:
						if (currentModule.Sources == null)
							currentModule.Sources = new SourcesInfo();
						SourcesInfo sources = currentModule.Sources;
						int subsectStart = (int)stm.Position;
						short numSources = rdr.ReadInt16();
						short numRanges = rdr.ReadInt16();
						int[] filesOffsets = new int[numSources];
						for (int i = 0; i < numSources; i++)
							filesOffsets[i] = rdr.ReadInt32();
						sources.Ranges = new SourcesRangeInfo[numRanges];
						for (int i = 0; i < numRanges; i++)
						{
							sources.Ranges[i].StartOffset = rdr.ReadInt32();
							sources.Ranges[i].EndOffset = rdr.ReadInt32();
						}
						for (int i = 0; i < numRanges; i++)
							sources.Ranges[i].SegIndex = rdr.ReadInt16();
						for (int i = 0; i < numSources; i++)
						{
							SourceFileInfo sourceFile = new SourceFileInfo();
							stm.Position = subsectStart + filesOffsets[i];
							int numSrcRanges = rdr.ReadInt16();
							int srcNameId = rdr.ReadInt32();
							int[] numbersOffsets = new int[numSrcRanges];
							sourceFile.Ranges = new SourceFileRangeInfo[numSrcRanges];
							for (int r = 0; r < numSrcRanges; r++)
								numbersOffsets[r] = rdr.ReadInt32();
							for (int r = 0; r < numSrcRanges; r++)
							{
								sourceFile.Ranges[r].StartOffset = rdr.ReadInt32();
								sourceFile.Ranges[r].EndOffset = rdr.ReadInt32();
							}
							for (int r = 0; r < numSrcRanges; r++)
							{
								stm.Position = subsectStart + numbersOffsets[r];
								sourceFile.Ranges[r].SegIndex = rdr.ReadInt16();
								short linesNum = rdr.ReadInt16();
								sourceFile.Ranges[r].Lines = new SourceLineInfo[linesNum];
								for (int l = 0; l < linesNum; l++)
									sourceFile.Ranges[r].Lines[l].Offset = rdr.ReadInt32();
								for (int l = 0; l < linesNum; l++)
									sourceFile.Ranges[r].Lines[l].LineNo = rdr.ReadInt16();
							}
							sourceFile.Name = NameFromId(srcNameId);
							sources.SourceFiles.Add(sourceFile);
						}
						break;
					case SubSectType.AlignSym:
						int alignSymUnk1 = rdr.ReadInt32();
						int end = info.Offset + info.Size;
						ParseSymbols(stm, rdr, currentModule.Symbols, info.Offset, end);
						break;
				}
			}
			foreach (SubSectInfo info in subSectInfos)
			{
				stm.Position = info.Offset;
				switch (info.SubSectType)
				{
					case SubSectType.GlobalSym:
						short symhash = rdr.ReadInt16();
						short addrhash = rdr.ReadInt16();
						int cbsymbols = rdr.ReadInt32();
						int cbsymhash = rdr.ReadInt32();
						int cbaddrhash = rdr.ReadInt32();
						int cudts = rdr.ReadInt32();
						int cothers = rdr.ReadInt32();
						int ctotal = rdr.ReadInt32();
						int cnamespaces = rdr.ReadInt32();
						int symbolsStart = (int)stm.Position;
						ParseSymbols(stm, rdr, globalSymbols, symbolsStart, symbolsStart + cbsymbols);
						break;
				}
			}
		}

		private static SymbolInfo LoadSymbol(Stream stm)
		{
			BinaryReader rdr = new BinaryReader(stm);
			short size = rdr.ReadInt16();
			int start = (int)stm.Position;
			SymbolType type = (SymbolType)rdr.ReadInt16();
			SymbolInfo symbol = null;
			switch (type)
			{
				case SymbolType.Compile:
					SymCompile compile = new SymCompile();
					symbol = compile;
					compile.Machine = (MachineType)rdr.ReadByte();
					compile.Language = (LanguageType)rdr.ReadByte();
					short flags = rdr.ReadInt16();
					compile.WithPCode = ((flags >> 0) & 1) == 1;
					compile.FpuPrecis = (FpuPrecision)((flags >> 1) & 3);
					compile.Fpu = (FpuType)((flags >> 3) & 3);
					compile.AmbientData = (AmbientType)((flags >> 5) & 7);
					compile.AmbientCode = (AmbientType)((flags >> 8) & 7);
					compile.Mode32 = ((flags >> 11) & 1) == 1;
					compile.IsCharSigned = ((flags >> 12) & 1) == 1;
					byte compilerLen = rdr.ReadByte();
					compile.Compiler = new string(rdr.ReadChars(compilerLen));
					break;
				case SymbolType.Register:
					symbol = SymRegister.Read(rdr);
					break;
				case SymbolType.Constant:
					SymConstant symConst = new SymConstant();
					symConst.DataType = TypeInfoFromId(rdr.ReadInt32());
					symConst.Name = NameFromId(rdr.ReadInt32());
					if (symConst.Name != null)
						symConst.Name = NameTranslator.Parse(symConst.Name).ToString();
					symConst.BrowserOffset = rdr.ReadInt32();
					symConst.Value = NumericLeaf.Read(rdr);
					symbol = symConst;
					break;
				case SymbolType.Udt:
					SymUdt udt = new SymUdt();
					symbol = udt;
					int udtTypeId = rdr.ReadInt32();
					udt.Type = TypeInfoFromId(udtTypeId);
					udt.Flags = rdr.ReadInt16();
					int udtNameId = rdr.ReadInt32();
					udt.Name = NameFromId(udtNameId);
					udt.BrowserOffset = rdr.ReadInt32();
					break;
				case SymbolType.SymSearch:
					SymSearch search = new SymSearch();
					symbol = search;
					search.Offset = rdr.ReadInt32();
					search.Segment = rdr.ReadInt16();
					search.CodeSyms = rdr.ReadInt16();
					search.DataSyms = rdr.ReadInt16();
					search.FirstData = rdr.ReadInt32();
					break;
				case SymbolType.End:
					symbol = new SymEnd();
					break;
				case SymbolType.GProcRef:
					SymGProcRef gprocref = new SymGProcRef();
					gprocref.Unknown1 = rdr.ReadInt32();
					int gprocrefType = rdr.ReadInt32();
					gprocref.ProcType = TypeInfoFromId(gprocrefType);
					int grpocrefName = rdr.ReadInt32();
					gprocref.Name = NameFromId(grpocrefName);
					if (gprocref.Name != null)
					{
						if (gprocref.Name.Length >= 5 && gprocref.Name.Substring(0, 5) != "@$xt$" &&
							gprocref.Name.Substring(0, 5) != "@$xp$")
						{
							gprocref.Name = NameTranslator.Parse(gprocref.Name).ToString();
						}
					}
					gprocref.Unknown2 = rdr.ReadInt32();
					gprocref.Offset = rdr.ReadInt32();
					gprocref.Segment = rdr.ReadInt16();
					gprocref.Unknown3 = rdr.ReadInt32();
					symbol = gprocref;
					break;
				case SymbolType.GDataRef:
					SymGDataRef gdataref = new SymGDataRef();
					gdataref.Unknown1 = rdr.ReadInt32();
					int gdatarefType = rdr.ReadInt32();
					gdataref.DataType = TypeInfoFromId(gdatarefType);
					int gdatarefName = rdr.ReadInt32();
					gdataref.Name = NameFromId(gdatarefName);
					gdataref.Unknown2 = rdr.ReadInt32();
					gdataref.Offset = rdr.ReadInt32();
					gdataref.Segment = rdr.ReadInt16();
					symbol = gdataref;
					break;
				case SymbolType.BpRelative32:
					SymBpRelative32Info bprel = new SymBpRelative32Info();
					bprel.Offset = rdr.ReadInt32();
					int bprelTypeId = rdr.ReadInt32();
					bprel.DataType = TypeInfoFromId2(bprelTypeId);
					int bprelNameId = rdr.ReadInt32();
					bprel.Name = NameFromId(bprelNameId);
					bprel.BrowserOffset = rdr.ReadInt32();
					symbol = bprel;
					break;
				case SymbolType.LData32:
				case SymbolType.GData32:
					SymData32 data32 = new SymData32();
					data32.IsGlobal = type == SymbolType.GData32;
					data32.Offset = rdr.ReadInt32();
					data32.Segment = rdr.ReadInt16();
					data32.Flags = rdr.ReadInt16();
					//am if (data32.Flags != 0)
					//	Debug.Fail("Not implemented flags in Data32 symbol: " + data32.Flags.ToString());
					data32.DataType = TypeInfoFromId2(rdr.ReadInt32());
					data32.Name = NameFromId(rdr.ReadInt32());
					if (data32.Name != null)
						data32.Name = NameTranslator.TranslateUdtName(data32.Name);
					data32.BrowserOffset = rdr.ReadInt32();
					symbol = data32;
					break;
				case SymbolType.LocalProc32:
				case SymbolType.GlobalProc32:
					SymProc32Info procInfo = new SymProc32Info();
					procInfo.IsGlobal = type == SymbolType.GlobalProc32;
					procInfo.NestOffsets = NestingOffsets.ReadFromTds(rdr, true);
					procInfo.Size = rdr.ReadInt32();
					procInfo.DebugStart = rdr.ReadInt32();
					procInfo.DebugEnd = rdr.ReadInt32();
					procInfo.Offset = rdr.ReadInt32();
					procInfo.Segment = rdr.ReadInt16();
					short procUnk2 = rdr.ReadInt16();
					int typeId = rdr.ReadInt32();
					//am procInfo.ProcType = TypeInfoFromId(typeId);
					int nameId = rdr.ReadInt32();
					procInfo.Name = NameFromId(nameId);
					if (procInfo.Name != null)
						procInfo.Name = NameTranslator.Parse(procInfo.Name).ToString();
					int procUnk3 = rdr.ReadInt32();
					symbol = procInfo;
					break;
				case SymbolType.Thunk32:
					SymThunk32 thunk32 = new SymThunk32();
					thunk32.NestOffsets = NestingOffsets.ReadFromTds(rdr, true);
					thunk32.Offset = rdr.ReadInt32();
					thunk32.Segment = rdr.ReadInt16();
					thunk32.Length = rdr.ReadInt16();
					byte thunk32Kind = rdr.ReadByte();
					if (thunk32Kind < 0 || 3 < thunk32Kind)
						Debug.Fail("Unknown thunk32 type: " + thunk32Kind.ToString());
					thunk32.Kind = (ThunkType)thunk32Kind;
					thunk32.Name = NameFromId(rdr.ReadInt32());
					switch (thunk32.Kind)
					{
						case ThunkType.NoType:
							break;
						case ThunkType.Adjustor:
							thunk32.Adjust = rdr.ReadInt32();
							thunk32.FunctionName = NameFromId(rdr.ReadInt32());
							break;
						default:
							Debug.Fail("Not implemented thunk32 type: " + thunk32.Kind.ToString());
							break;
					}
					symbol = thunk32;
					break;
				case SymbolType.Block32:
					symbol = SymBlock32.Read(rdr);
					break;
				case SymbolType.With32:
					symbol = SymWith32.Read(rdr);
					break;
				case SymbolType.Label32:
					symbol = SymLabel32.Read(rdr);
					break;
				case SymbolType.Entry32:
					SymEntry32 symentry = new SymEntry32();
					symentry.Offset = rdr.ReadInt32();
					symentry.Segment = rdr.ReadInt16();
					symbol = symentry;
					break;
				case SymbolType.OptVar32:
					symbol = SymOptVar32.Read(rdr);
					break;
				case SymbolType.ProcRet32:
					SymProcRet32 symProcRet = new SymProcRet32();
					symProcRet.Offset = rdr.ReadInt32();
					symProcRet.Length = rdr.ReadInt16();
					symbol = symProcRet;
					break;
				case SymbolType.SaveRegs32:
					SymSaveRegs32 saveregs = new SymSaveRegs32();
					saveregs.Flags = rdr.ReadInt16();
					symbol = saveregs;
					break;
				case SymbolType.Uses:
					symbol = SymUses.Read(size, rdr);
					break;
				case SymbolType.Namespace:
					SymNamespace symNamespace = new SymNamespace();
					symNamespace.Name = NameFromId(rdr.ReadInt32());
					symNamespace.BrowserOffset = rdr.ReadInt32();
					symNamespace.UsingCount = rdr.ReadInt16();
					symbol = symNamespace;
					break;
				case SymbolType.Using:
					SymUsing symUsing = new SymUsing();
					symbol = symUsing;
					short numUsingNames = rdr.ReadInt16();
					for (int i = 0; i < numUsingNames; i++)
						symUsing.Names.Add(NameFromId(rdr.ReadInt32()));
					break;
				case SymbolType.PConstant:
					symbol = SymPConstant.Read(rdr);
					break;
				case SymbolType.SLink32:
					symbol = SymSLink32.Read(rdr);
					break;
				default:
					Debug.Fail("Unknown symbol: " + type.ToString());
					break;
			}
			stm.Position = start + size;
			return symbol;
		}

		private static void ParseSymbols(FileStream stm, BinaryReader rdr, ICollection<SymbolInfo> symbols, int sectionStart, int end)
		{
			SortedDictionary<int, SymbolInfo> offsetToSymInfo = new SortedDictionary<int, SymbolInfo>();
			while (stm.Position != end)
			{
				int symbolOffset = (int)stm.Position - sectionStart;
				SymbolInfo symbol = LoadSymbol(stm);
				symbols.Add(symbol);
				offsetToSymInfo.Add(symbolOffset, symbol);
			}
			foreach (SymbolInfo symInfo in offsetToSymInfo.Values)
			{
				SymScopedInfo scoped = symInfo as SymScopedInfo;
				if (scoped == null)
					continue;
				int scopeparent = scoped.NestOffsets.Parent;
				if (scopeparent != 0)
					scoped.Parent = offsetToSymInfo[scopeparent];
				int scopeend = scoped.NestOffsets.End;
				if (scopeend != 0)
					scoped.End = offsetToSymInfo[scopeend];
				if (scoped.NestOffsets.HasNext)
				{
					int scopenext = scoped.NestOffsets.Next;
					if (scopenext != 0)
						scoped.Next = offsetToSymInfo[scopenext];
				}
				scoped.NestOffsets = null;
			}
		}
	}
}
