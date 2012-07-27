using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace tds2pdbproto
{
	public abstract class TemplateParam
	{
		public abstract TemplateParam GetLeafType();
	}

	public class ImmTemplParam : TemplateParam
	{
		public object Value;

		public ImmTemplParam(object value)
		{
			Value = value;
		}

		public override TemplateParam GetLeafType()
		{
			return this;
		}

		public override string ToString()
		{
			return Value.ToString();
		}
	}

	public class TagTemplParam : TemplateParam
	{
		public Name Tag;

		public override TemplateParam GetLeafType()
		{
			return this;
		}

		public override string ToString()
		{
			return Tag.ToString();
		}
	}

	public class PrimitiveTemplParam : TemplateParam
	{
		public string Primitive;
		public bool Unsigned;
		public bool Signed;

		public override TemplateParam GetLeafType()
		{
			return this;
		}
		
		public PrimitiveTemplParam(string primitive)
		{
			Primitive = primitive;
		}

		public override string ToString()
		{
			return (Unsigned ? "unsigned " : "") + (Signed ? "signed " : "") + Primitive;
		}
	}

	public class ComplexTemplParam : TemplateParam
	{
		public TemplateParam InternalType;

		public override TemplateParam GetLeafType()
		{
			return InternalType.GetLeafType();
		}

		public void Combine(TemplateParam param)
		{
			InternalType = param;
		}

		public virtual void Combine(ComplexTemplParam param, out ComplexTemplParam current)
		{
			InternalType = param;
			current = param;
		}
	}

	public class PointerTemplParam : ComplexTemplParam
	{
		public bool IsReference = false;

		public bool IsFinallyPointsToFunction()
		{
			if (InternalType is FuncTemplParam)
				return true;
			else if (InternalType is PointerTemplParam)
				return ((PointerTemplParam)InternalType).IsFinallyPointsToFunction();
			else if (InternalType is ModifierTemplParam)
				return ((ModifierTemplParam)InternalType).IsFinallyPointsToFunction();
			else
				return false;
		}

		internal string RenderFuncStr(string modifiers)
		{
			if (modifiers.Length != 0)
				modifiers = " " + modifiers;
			modifiers = (IsReference ? "&" : "*") + modifiers;
			if (InternalType is FuncTemplParam)
				return ((FuncTemplParam)InternalType).RenderFuncStr(modifiers);
			else if (InternalType is PointerTemplParam)
				return ((PointerTemplParam)InternalType).RenderFuncStr(modifiers);
			else if (InternalType is ModifierTemplParam)
				return ((ModifierTemplParam)InternalType).RenderFuncStr(modifiers);
			else
				throw new ApplicationException("Invalid internal type for pointer in RenderFuncStr: " + InternalType.ToString());
		}

		public override string ToString()
		{
			if (IsFinallyPointsToFunction())
				return RenderFuncStr("");
			else
				return InternalType.ToString() + (IsReference ? " &" : " *");
		}
	}

	public class ModifierTemplParam : ComplexTemplParam
	{
		public ModifierAttributes Attributes;

		public bool IsFinallyPointsToFunction()
		{
			if (InternalType is PointerTemplParam)
				return ((PointerTemplParam)InternalType).IsFinallyPointsToFunction();
			else if (InternalType is ModifierTemplParam)
				return ((ModifierTemplParam)InternalType).IsFinallyPointsToFunction();
			else
				return false;
		}

		public override void Combine(ComplexTemplParam param, out ComplexTemplParam current)
		{
			if (param is ModifierTemplParam)
			{
				ModifierTemplParam other = param as ModifierTemplParam;
				Attributes |= other.Attributes;
				current = this;
			}
			else
			{
				base.Combine(param, out current);
			}
		}

		private string GetModifiersStr()
		{
			string[] strings = new string[2];
			int numStrings = 0;
			if ((Attributes & ModifierAttributes.Const) != 0)
			{
				strings[numStrings] = "const";
				numStrings++;
			}
			if ((Attributes & ModifierAttributes.Volatile) != 0)
			{
				strings[numStrings] = "volatile";
				numStrings++;
			}
			return string.Join(" ", strings, 0, numStrings);
		}

		internal string RenderFuncStr(string modifiers)
		{
			if (modifiers.Length != 0)
				modifiers = " " + modifiers;
			modifiers = GetModifiersStr() + modifiers;
			if (InternalType is FuncTemplParam)
				return ((FuncTemplParam)InternalType).RenderFuncStr(modifiers);
			else if (InternalType is PointerTemplParam)
				return ((PointerTemplParam)InternalType).RenderFuncStr(modifiers);
			else if (InternalType is ModifierTemplParam)
				return ((ModifierTemplParam)InternalType).RenderFuncStr(modifiers);
			else
				throw new ApplicationException("Invalid internal type for pointer in RenderFuncStr: " + InternalType.ToString());
		}

		public override string ToString()
		{
			if (IsFinallyPointsToFunction())
				return RenderFuncStr("");
			else
				return InternalType.ToString() + " " + GetModifiersStr();
		}
	}

	public class FuncTemplParam : TemplateParam
	{
		public TemplateParam ReturnType;
		public List<TemplateParam> ArgsTypes = new List<TemplateParam>();

		public override TemplateParam GetLeafType()
		{
			return this;
		}

		internal string RenderFuncStr(string modifiers)
		{
			string[] args = new string[ArgsTypes.Count];
			for (int i = 0; i < args.Length; i++)
				args[i] = ArgsTypes[i].ToString();
			return ReturnType.ToString() + "(" + modifiers + ")(" + string.Join(", ", args) + ")";
		}

		public override string ToString()
		{
			return RenderFuncStr("");
		}
	}

	public enum SpecialNameType
	{
		Constructor,
		Destructor,
		// arithmetic operators
		OpAdd,
		OpSub,
		OpMul,
		OpDiv,
		OpMod,
		OpInc,
		OpDec,
		// assignment operators
		OpAsg, // =
		// arithmetics
		OpRplu, // +=
		OpRmin, // -=
		OpRmul, // *=
		OpRdiv, // *=
		OpRmod, // %=
		// bitwise
		OpRor, // |=
		OpRand, // &=
		OpRxor, // ^=
		OpRLeftShift, // <<=
		OpRRightShift, // >>=
		// bitwise operators
		OpCmp,
		OpOr,
		OpAnd,
		OpXor,
		OpLeftShift,
		OpRightShift,
		// logical operators
		OpNot,
		OpEql,
		OpNeq,
		OpLss,
		OpLeq,
		OpGtr,
		OpGeq,
		// other
		OpAdr, // &
		OpArrow, // ->
		OpSubscript, // []
		OpCall, // ()
		OpIndirect, // *
		OpNew, // operator new
		OpNewArray, // operator new[]
		OpDelete, // operator delete
		OpDeleteArray, // operator delete[]
	}

	public class Name
	{
		public List<Name> Namespaces = null;
		public string Tag = "";
		public List<TemplateParam> TemplateParameters = null;
		public bool IsSpecial = false;
		public SpecialNameType SpecialName;

		public override string ToString()
		{
			StringBuilder result = new StringBuilder();
			if (Namespaces != null)
			{
				foreach (Name namepart in Namespaces)
				{
					result.Append(namepart.ToString());
					result.Append("::");
				}
			}
			if (IsSpecial)
			{
				switch (SpecialName)
				{
					case SpecialNameType.Constructor:
						result.Append(Namespaces[Namespaces.Count - 1]);
						break;
					case SpecialNameType.Destructor:
						result.Append('~');
						result.Append(Namespaces[Namespaces.Count - 1]);
						break;
					case SpecialNameType.OpAdd:
						result.Append("operator+");
						break;
					case SpecialNameType.OpSub:
						result.Append("operator-");
						break;
					case SpecialNameType.OpMul:
						result.Append("operator*");
						break;
					case SpecialNameType.OpDiv:
						result.Append("operator/");
						break;
					case SpecialNameType.OpMod:
						result.Append("operator%");
						break;
					case SpecialNameType.OpInc:
						result.Append("operator++");
						break;
					case SpecialNameType.OpDec:
						result.Append("operator--");
						break;

					case SpecialNameType.OpAsg:
						result.Append("operator=");
						break;
					case SpecialNameType.OpRplu:
						result.Append("operator+=");
						break;
					case SpecialNameType.OpRmin:
						result.Append("operator-=");
						break;
					case SpecialNameType.OpRmul:
						result.Append("operator*=");
						break;
					case SpecialNameType.OpRdiv:
						result.Append("operator/=");
						break;
					case SpecialNameType.OpRmod:
						result.Append("operator%=");
						break;
					case SpecialNameType.OpRor:
						result.Append("operator|=");
						break;
					case SpecialNameType.OpRand:
						result.Append("operator&=");
						break;
					case SpecialNameType.OpRxor:
						result.Append("operator^=");
						break;
					case SpecialNameType.OpRLeftShift:
						result.Append("operator<<=");
						break;
					case SpecialNameType.OpRRightShift:
						result.Append("operator>>=");
						break;

					case SpecialNameType.OpCmp:
						result.Append("operator~");
						break;
					case SpecialNameType.OpOr:
						result.Append("operator|");
						break;
					case SpecialNameType.OpAnd:
						result.Append("operator&");
						break;
					case SpecialNameType.OpXor:
						result.Append("operator^");
						break;
					case SpecialNameType.OpLeftShift:
						result.Append("operator<<");
						break;
					case SpecialNameType.OpRightShift:
						result.Append("operator>>");
						break;

					case SpecialNameType.OpNot:
						result.Append("operator!");
						break;
					case SpecialNameType.OpEql:
						result.Append("operator==");
						break;
					case SpecialNameType.OpNeq:
						result.Append("operator!=");
						break;
					case SpecialNameType.OpLss:
						result.Append("operator<");
						break;
					case SpecialNameType.OpLeq:
						result.Append("operator<=");
						break;
					case SpecialNameType.OpGtr:
						result.Append("operator>");
						break;
					case SpecialNameType.OpGeq:
						result.Append("operator>=");
						break;

					case SpecialNameType.OpAdr:
						result.Append("operator&");
						break;
					case SpecialNameType.OpArrow:
						result.Append("operator->");
						break;
					case SpecialNameType.OpSubscript:
						result.Append("operator[]");
						break;
					case SpecialNameType.OpCall:
						result.Append("operator()");
						break;
					case SpecialNameType.OpIndirect:
						result.Append("operator*");
						break;
					case SpecialNameType.OpDelete:
						result.Append("operator delete");
						break;
					case SpecialNameType.OpDeleteArray:
						result.Append("operator delete[]");
						break;
					case SpecialNameType.OpNew:
						result.Append("operator new");
						break;
					case SpecialNameType.OpNewArray:
						result.Append("operator new[]");
						break;
					default:
                        throw new ApplicationException("Unknown special name type: " + SpecialName);
				}
			}
			else
			{
				result.Append(Tag);
			}
			if (TemplateParameters != null)
			{
				result.Append("<");
				string[] pars = new string[TemplateParameters.Count];
				int i = 0;
				foreach (TemplateParam par in TemplateParameters)
				{
					pars[i] = par.ToString();
					i++;
				}
				result.Append(string.Join(",", pars));
				result.Append(">");
			}
			return result.ToString();
		}
	}

	public class NameTranslator
	{
		public static string TranslateUdtName(string strname)
		{
			return Parse(strname).ToString();
		}

		public static Name Parse(string strname)
		{
			if (strname[0] == '@')
			{
				strname = strname.Substring(1);
				Name result;
				try
				{
					result = TranslateUdtNameV2(strname);
				}
				catch (Exception exv2)
				{
					try
					{
						result = TranslateUdtNameV1(strname);
					}
					catch (Exception exv1)
					{
						if (strname.Length >= 253)
						{
							Console.WriteLine("Warning: Name cannot be parsed: " + strname + " possible reason - incomplete");
							Name name = new Name();
							name.Tag = strname;
							return name;
						}
						else
						{
							Console.WriteLine("Warning: Name cannot be parsed: " + strname);
							//Console.WriteLine("  Error v2: " + exv2.Message);
							//Console.WriteLine("  Error v1: " + exv1.Message);
							Name name = new Name();
							name.Tag = strname;
							return name;
						}
					}
				}
				return result;
			}
			else
			{
				Name name = new Name();
				name.Tag = strname;
				return name;
			}
		}

		private static Name TranslateUdtNameV1(string strname)
		{
			StringBuilder tag = new StringBuilder();
			Name result = new Name();
			List<TemplateParam> templateParams = null;
			int state = 0;
			//int param = 0;
			//bool unsigned = false;
			StringBuilder constTemplParam = null;
			int templParamUdtNameLen = 0;
			StringBuilder specialName = null;

			int i = 0;
			for (; i < strname.Length; i++)
			{
				char ch = strname[i];
				switch (state)
				{
					case 0:
						switch (ch)
						{
							case '@':
								if (result.Namespaces == null)
									result.Namespaces = new List<Name>();
								Name currentName = new Name();
								currentName.Tag = tag.ToString();
								currentName.TemplateParameters = templateParams;
								result.Namespaces.Add(currentName);
								tag = new StringBuilder();
								templateParams = null;
								break;
							case '%':
								if (tag.Length == 0)
								{
									templateParams = new List<TemplateParam>();
									state = 1;
								}
								else
								{
									throw new ApplicationException("Invalid character in state 0:" + ch);
								}
								break;
							case '$':
								if (tag.Length == 0)
									state = 12; // special name
								else
									state = 14; // parameters
								break;
							default:
								tag.Append(ch);
								break;
						}
						break;
					case 1:
						switch (ch)
						{
							case '$':
								state = 2;
								break;
							default:
								tag.Append(ch);
								break;
						}
						break;
					case 2:
						switch (ch)
						{
							case 'i':
								state = 9;
								break;
							case 'u':
								//unsigned = true;
								break;
							case 't':
								templParamUdtNameLen = 0;
								state = 8;
								break;
							case '%':
								state = 0;
								break;
							default:
								throw new ApplicationException("Invalid character while in state 2: " + ch + ", in string: " + strname);
						}
						break;
					case 5: // constant in template
						if (char.IsDigit(ch))
						{
							constTemplParam.Append(ch);
						}
						else
						{
							templateParams.Add(new ImmTemplParam(constTemplParam.ToString()));
							state = 11;
							i--;
						}
						break;
					case 8:
						if (char.IsDigit(ch))
						{
							templParamUdtNameLen *= 10;
							templParamUdtNameLen += (int)char.GetNumericValue(ch);
						}
						else
						{
							//if (strname.Length < i + templParamUdtNameLen)
							//	throw new ApplicationException("Invalid type name length: " + templParamUdtNameLen.ToString() + ", exceed name length: " + strname.Length.ToString());
							TagTemplParam tagpar = new TagTemplParam();
							tagpar.Tag = TranslateUdtNameV1(strname.Substring(i, templParamUdtNameLen));
							templateParams.Add(tagpar);
							i += templParamUdtNameLen - 1;
							state = 11;
						}
						break;
					case 9:
						switch (ch)
						{
							case 'c':
								//param = 2;
								state = 10;
								break;
							case 'u':
								//unsigned = true;
								break;
                            //default: ;
							//	throw new ApplicationException("Invalid character while in state 2: " + ch + ", in string: " + strname);
						}
						break;
					case 10:
						switch (ch)
						{
							case '$':
								state = 5;
								constTemplParam = new StringBuilder();
								break;
							default:
								throw new ApplicationException("Invalid character while in state " + state.ToString() + ": " + ch + ", in string: " + strname);
						}
						break;
					case 11:
						switch (ch)
						{
							case '$':
								state = 2;
								break;
							case '%':
								state = 2;
								i--;
								break;
						}
						break;
					case 12:
						if (ch == 'b')
						{
							state = 13; // special name
							specialName = new StringBuilder();
						}
						else
						{
							throw new ApplicationException("Invalid character in state 9: " + ch);
						}
						break;
					case 13:
						if (ch == '$')
						{
							result.IsSpecial = true;
							result.SpecialName = StrToSpcName(specialName.ToString());
							specialName = null;
							state = 14;
							break;
						}
						else
						{
							specialName.Append(ch);
						}
						break;
					case 14:
						// ignoring parameters
						break;
					default:
						Debug.Fail("Invalid state: " + state.ToString());
						break;
				}
			}
			if (state != 0 && state != 14)
				throw new ApplicationException("Unexpected end of name while in state: " + state.ToString());
			result.Tag = tag.ToString();
			result.TemplateParameters = templateParams;
			return result;
		}

		private static SpecialNameType StrToSpcName(string spcname)
		{
			switch (spcname)
			{
				case "ctr": return SpecialNameType.Constructor;
				case "dtr": return SpecialNameType.Destructor;
				case "add": return SpecialNameType.OpAdd;
				case "sub": return SpecialNameType.OpSub;
				case "mul": return SpecialNameType.OpMul;
				case "div": return SpecialNameType.OpDiv;
				case "mod": return SpecialNameType.OpMod;
				case "inc": return SpecialNameType.OpInc;
				case "dec": return SpecialNameType.OpDec;

				case "asg": return SpecialNameType.OpAsg;
				case "rplu": return SpecialNameType.OpRplu;
				case "rmin": return SpecialNameType.OpRmin;
				case "rmul": return SpecialNameType.OpRmul;
				case "rdiv": return SpecialNameType.OpRdiv;
				case "rmod": return SpecialNameType.OpRmod;
				case "ror": return SpecialNameType.OpRor;
				case "rand": return SpecialNameType.OpRand;
				case "rxor": return SpecialNameType.OpRxor;
				case "rlsh": return SpecialNameType.OpRLeftShift;
				case "rrsh": return SpecialNameType.OpRRightShift;

				case "cmp": return SpecialNameType.OpCmp;
				case "or": return SpecialNameType.OpOr;
				case "and": return SpecialNameType.OpAnd;
				case "xor": return SpecialNameType.OpXor;
				case "lsh": return SpecialNameType.OpLeftShift;
				case "rsh": return SpecialNameType.OpRightShift;

				case "not": return SpecialNameType.OpNot;
				case "eql": return SpecialNameType.OpEql;
				case "neq": return SpecialNameType.OpNeq;
				case "lss": return SpecialNameType.OpLss;
				case "leq": return SpecialNameType.OpLeq;
				case "gtr": return SpecialNameType.OpGtr;
				case "geq": return SpecialNameType.OpGeq;

				case "adr": return SpecialNameType.OpAdr;
				case "arow": return SpecialNameType.OpArrow;
				case "subs": return SpecialNameType.OpSubscript;
				case "call": return SpecialNameType.OpCall;
				case "ind": return SpecialNameType.OpIndirect;
				case "new": return SpecialNameType.OpNew;
				case "nwa": return SpecialNameType.OpNewArray;
				case "dele": return SpecialNameType.OpDelete;
				case "dla": return SpecialNameType.OpDeleteArray;

				default: throw new ApplicationException("Unknown special name: " + spcname);
			}
		}

		private static PrimitiveTemplParam TryParsePrimitive(char ch)
		{
			switch (ch)
			{
				case 'c': return new PrimitiveTemplParam("char");
				case 'b': return new PrimitiveTemplParam("wchar_t");
				case 's': return new PrimitiveTemplParam("short");
				case 'v': return new PrimitiveTemplParam("void");
				case 'i': return new PrimitiveTemplParam("int");
				case 'l': return new PrimitiveTemplParam("long");
				case 'j': return new PrimitiveTemplParam("__int64");
				case 'f': return new PrimitiveTemplParam("float");
				case 'd': return new PrimitiveTemplParam("double");
				case 'g': return new PrimitiveTemplParam("long double");
				case 'o': return new PrimitiveTemplParam("bool");
				default: return null;
			}
		}

		private static FuncTemplParam ParseFuncParam(string strname, ref int pos)
		{
			FuncTemplParam func = new FuncTemplParam();
			for (; pos < strname.Length; pos++)
			{
				char ch = strname[pos];
				if (ch == '$')
				{
					pos++;
					func.ReturnType = ParseParam(strname, ref pos);
					return func;
				}
				else
				{
					if (ch == 't')
					{
						pos++;
						int paramIndex = (int)char.GetNumericValue(strname[pos]) - 1;
						func.ArgsTypes.Add(func.ArgsTypes[paramIndex]);
					}
					else
					{
						func.ArgsTypes.Add(ParseParam(strname, ref pos));
					}
				}
			}
			throw new ApplicationException("Cannot parse function type, unexpected end of string");
		}

		private static TemplateParam ParseParam(string strname, ref int pos)
		{
			ComplexTemplParam topComplex = null;
			ComplexTemplParam currentComplex = null;
			int state = 0;
			bool signed = false;
			bool unsigned = false;
			int templParamUdtNameLen = 0;
			for (; pos < strname.Length; pos++)
			{
				char ch = strname[pos];
				switch (state)
				{
					case 0:
						if (char.IsDigit(ch))
						{
							templParamUdtNameLen = (int)char.GetNumericValue(ch);
							state = 1;
						}
						else
						{
							PrimitiveTemplParam prim = TryParsePrimitive(ch);
							if (prim != null)
							{
								prim.Signed = signed;
								prim.Unsigned = unsigned;
								signed = false;
								unsigned = false;
								if (currentComplex != null)
								{
									currentComplex.Combine(prim);
									return topComplex;
								}
								else
								{
									return prim;
								}
							}
							else
							{
								switch (ch)
								{
									case 'u':
										unsigned = true;
										break;
									case 'z':
										signed = true;
										break;
									case 'x':
									case 'w':
										ModifierTemplParam newModifier = new ModifierTemplParam();
										if (ch == 'x')
											newModifier.Attributes |= ModifierAttributes.Const;
										else if (ch == 'w')
											newModifier.Attributes |= ModifierAttributes.Volatile;
										if (topComplex == null)
										{
											topComplex = newModifier;
											currentComplex = newModifier;
										}
										else
										{
											currentComplex.Combine(newModifier, out currentComplex);
										}
										break;
									case 'p':
									case 'r':
										PointerTemplParam newPointer = new PointerTemplParam();
										newPointer.IsReference = ch == 'r';
										if (topComplex == null)
										{
											topComplex = newPointer;
											currentComplex = newPointer;
										}
										else
										{
											currentComplex.Combine(newPointer, out currentComplex);
										}
										break;
									case 'q':
										pos++;
										FuncTemplParam funcParam = ParseFuncParam(strname, ref pos);
										if (currentComplex != null)
										{
											currentComplex.Combine(funcParam);
											return topComplex;
										}
										else
										{
											return funcParam;
										}
									default:
										pos--;
										return null;
								}
							}
						}
						break;
					case 1:
						if (char.IsDigit(ch))
						{
							templParamUdtNameLen *= 10;
							templParamUdtNameLen += (int)char.GetNumericValue(ch);
						}
						else
						{
							if (strname.Length < pos + templParamUdtNameLen)
								throw new ApplicationException("Invalid type name length: " + templParamUdtNameLen.ToString() + ", exceed name length: " + strname.Length.ToString());
							TagTemplParam tagTemplParam = new TagTemplParam();
							tagTemplParam.Tag = TranslateUdtNameV2(strname.Substring(pos, templParamUdtNameLen));
							pos += templParamUdtNameLen - 1;
							if (currentComplex != null)
							{
								currentComplex.Combine(tagTemplParam);
								return topComplex;
							}
							else
							{
								return tagTemplParam;
							}
						}
						break;
				}
			}
			return null;
		}

		private static Name TranslateUdtNameV2(string strname)
		{
			Name result = new Name();
			StringBuilder tag = new StringBuilder();
			List<TemplateParam> templateParams = null;
			int state = 0;
			StringBuilder constTemplParam = null;
			StringBuilder specialName = null;
			TemplateParam templParam = null;

			int i = 0;
			for (; i < strname.Length; i++)
			{
				char ch = strname[i];
				switch (state)
				{
					case 0:
						switch (ch)
						{
							case '@':
								if (result.Namespaces == null)
									result.Namespaces = new List<Name>();
								Name currentName = new Name();
								currentName.Tag = tag.ToString();
								currentName.TemplateParameters = templateParams;
								result.Namespaces.Add(currentName);
								tag = new StringBuilder();
								templateParams = null;
								break;
							case '%':
								templateParams = new List<TemplateParam>();
								state = 1;
								break;
							case '$':
								if (tag.Length == 0)
									state = 9;
								else
									state = 11; // parameters
								break;
							default:
								tag.Append(ch);
								break;
						}
						break;
					case 1:
						switch (ch)
						{
							case '$':
								state = 2;
								break;
							default:
								tag.Append(ch);
								break;
						}
						break;
					case 2:
						switch (ch)
						{
							case 't':
								state = 12;
								break;
							case '%':
								state = 0;
								break;
							default:
								templParam = ParseParam(strname, ref i);
								if (templParam != null)
									state = 3;
								else
									throw new ApplicationException("Invalid character while in state 2: " + ch + ", in string: " + strname);
								break;
						}
						break;
					case 3: // template param type grabbed
						switch (ch)
						{
							case '$':
								state = 4;
								break;
							default:
								state = 2;
								i--;
								templateParams.Add(templParam);
								break;
						}
						break;
					case 4: // begin constant in template
						switch (ch)
						{
							case 'i': // numeric constant
								state = 5;
								constTemplParam = new StringBuilder();
								break;
							case 'e': // reference to static variable
								i++;
								templateParams.Add(ParseTemplVarRefParam(templParam, strname, ref i));
								state = 2;
								break;
							case 'g':
								state = 5;
								constTemplParam = new StringBuilder();
								break;
							default:
								throw new ApplicationException("Invalid character while in state 4: " + ch + ", in string: " + strname);
						}
						break;
					case 5: // constant in template
						if (ch == '$')
						{
							templateParams.Add(new ImmTemplParam(constTemplParam.ToString()));
							state = 2;
						}
						else
						{
							constTemplParam.Append(ch);
						}
						break;
					case 9:
						if (ch == 'b')
						{
							state = 10; // special name
							specialName = new StringBuilder();
						}
						else
						{
							throw new ApplicationException("Invalid character in state 9: " + ch);
						}
						break;
					case 10:
						if (ch == '$')
						{
							result.IsSpecial = true;
							result.SpecialName = StrToSpcName(specialName.ToString());
							specialName = null;
							state = 11;
							break;
						}
						else
						{
							specialName.Append(ch);
						}
						break;
					case 11:
						// ignoring parameters
						break;
					case 12:
						templateParams.Add(templateParams[(int)char.GetNumericValue(ch) - 1]);
						state = 2;
						break;
					default:
						Debug.Fail("Invalid state: " + state.ToString());
						break;
				}
			}
			if (state != 0 && state != 11)
				throw new ApplicationException("Unexpected end of name while in state: " + state.ToString());
			result.Tag = tag.ToString();
			result.TemplateParameters = templateParams;
			return result;
		}

		private static TemplateParam ParseTemplVarRefParam(TemplateParam paramType, string strname, ref int pos)
		{
			StringBuilder varrefstr = new StringBuilder();
			TemplateParam leafType = paramType.GetLeafType();
			bool isGuid = leafType is TagTemplParam && ((TagTemplParam)leafType).Tag.ToString() == "_GUID";
			int dollarPos = -1;
			int beforeDollarLen = 0;
			int state = 0;
			for (; pos < strname.Length; pos++)
			{
				char ch = strname[pos];
				switch (state)
				{
					case 0:
						if (ch == '$')
						{
							if (isGuid)
							{
								dollarPos = pos;
								beforeDollarLen = varrefstr.Length;
								varrefstr.Append('_');
								state = 1;
							}
							else
							{
								return new ImmTemplParam(varrefstr.ToString());
							}
						}
						else
						{
							varrefstr.Append(ch);
						}
						break;
					case 1:
						if (ch == '$')
						{
							return new ImmTemplParam(varrefstr.ToString());
						}
						else if (!char.IsLetter(ch) || !char.IsUpper(ch))
						{
							pos = dollarPos;
							varrefstr.Length = dollarPos;
							return new ImmTemplParam(varrefstr.ToString());
						}
						else
						{
							varrefstr.Append(ch);
						}
						break;
				}
			}
			throw new ApplicationException("Unexpected end of string while parsing variable reference in template parameters");
		}
	}
}
