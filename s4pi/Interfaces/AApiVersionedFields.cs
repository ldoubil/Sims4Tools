/***************************************************************************
 *  Copyright (C) 2009, 2015 by the Sims 4 Tools development team          *
 *                                                                         *
 *  Contributors:                                                          *
 *  Peter L Jones (pljones@users.sf.net)                                   *
 *  Keyi Zhang                                                             *
 *                                                                         *
 *  This file is part of the Sims 4 Package Interface (s4pi)               *
 *                                                                         *
 *  s4pi is free software: you can redistribute it and/or modify           *
 *  it under the terms of the GNU General Public License as published by   *
 *  the Free Software Foundation, either version 3 of the License, or      *
 *  (at your option) any later version.                                    *
 *                                                                         *
 *  s4pi is distributed in the hope that it will be useful,                *
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of         *
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the          *
 *  GNU General Public License for more details.                           *
 *                                                                         *
 *  You should have received a copy of the GNU General Public License      *
 *  along with s4pi.  If not, see <http://www.gnu.org/licenses/>.          *
 ***************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace s4pi.Interfaces
{
    using System.Globalization;

    /// <summary>
	///     API Objects should all descend from this Abstract class.
	///     It will provide versioning support -- when implemented.
	///     It provides ContentFields support
	/// </summary>
	public abstract class AApiVersionedFields : IApiVersion, IContentFields
	{
		#region IApiVersion Members

		/// <summary>
		///     The version of the API in use
		/// </summary>
		public int RequestedApiVersion
		{
			get { return this.requestedApiVersion; }
		}

		/// <summary>
		///     The best supported version of the API available
		/// </summary>
		public abstract int RecommendedApiVersion { get; }

		#endregion

		#region IContentFields Members

		/// <summary>
		///     The list of available field names on this API object
		/// </summary>
		public abstract List<string> ContentFields { get; }

		// This should be implemented with a call to GetContentFields(requestedApiVersion, this.GetType())

		/// <summary>
		///     A typed value on this object
		/// </summary>
		/// <param name="index">The name of the field (value.e. one of the values from ContentFields)</param>
		/// <returns>The typed value of the named field</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when an unknown index name is requested</exception>
		public virtual TypedValue this[string index]
		{
			get
			{
				string[] fields = index.Split('.');
				object result = this;
				Type t = this.GetType();
				foreach (string field in fields)
				{
					PropertyInfo p = t.GetProperty(field);
					if (p == null)
					{
						throw new ArgumentOutOfRangeException("index", "Unexpected value received in index: " + index);
					}
					t = p.PropertyType;
					result = p.GetValue(result, null);
				}
				return new TypedValue(t, result, "X");
			}
			set
			{
				string[] fields = index.Split('.');
				object result = this;
				Type t = this.GetType();
				PropertyInfo p = null;
				for (int i = 0; i < fields.Length; i++)
				{
					p = t.GetProperty(fields[i]);
					if (p == null)
					{
						throw new ArgumentOutOfRangeException("index", "Unexpected value received in index: " + index);
					}
					if (i < fields.Length - 1)
					{
						t = p.PropertyType;
						result = p.GetValue(result, null);
					}
				}
				p.SetValue(result, value.Value, null);
			}
		}

		#endregion

		/// <summary>
		///     Versioning is not currently implemented
		///     Set this to the version of the API requested on object creation
		/// </summary>
		protected int requestedApiVersion;

		private static List<string> banlist;

		static AApiVersionedFields()
		{
			Type t = typeof(AApiVersionedFields);
			banlist = new List<string>();
			foreach (PropertyInfo m in t.GetProperties())
			{
				banlist.Add(m.Name);
			}
		}

		private static int Version(Type attribute, Type type, string field)
		{
		    int version = (type.GetProperty(field)
		                       .GetCustomAttributes(attribute, true)
		                       .Cast<VersionAttribute>()
		                       .Select(attr => attr.Version))
                           .FirstOrDefault();

		    return version;
		}

	    private static int MinimumVersion(Type type, string field)
		{
			return Version(typeof(MinimumVersionAttribute), type, field);
		}

		private static int MaximumVersion(Type type, string field)
		{
			return Version(typeof(MaximumVersionAttribute), type, field);
		}

		private static int GetRecommendedApiVersion(Type t)
		{
			FieldInfo fi = t.GetField("recommendedApiVersion", BindingFlags.Static | BindingFlags.NonPublic);
			if (fi == null || fi.FieldType != typeof(int))
			{
				return 0;
			}
			return (int)fi.GetValue(null);
		}

		private static bool CheckVersion(Type type, string field, int requestedApiVersion)
		{
			if (requestedApiVersion == 0)
			{
				return true;
			}
			int min = MinimumVersion(type, field);
			if (min != 0 && requestedApiVersion < min)
			{
				return false;
			}
			int max = MaximumVersion(type, field);
			if (max != 0 && requestedApiVersion > max)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		///     Versioning is not currently implemented
		///     Return the list of fields for a given API Class
		/// </summary>
		/// <param name="APIversion">Set to 0 (== "best")</param>
		/// <param name="t">The class type for which to get the fields</param>
		/// <returns>List of field names for the given API version</returns>
		public static List<string> GetContentFields(int APIversion, Type t)
		{
			List<string> fields = new List<string>();

			int recommendedApiVersion = GetRecommendedApiVersion(t);
			//Could be zero if no "recommendedApiVersion" const field
			PropertyInfo[] ap = t.GetProperties();
			foreach (PropertyInfo info in ap)
			{
				if (banlist.Contains(info.Name))
				{
					continue;
				}
				if (!CheckVersion(t, info.Name, APIversion == 0 ? recommendedApiVersion : APIversion))
				{
					continue;
				}

				fields.Add(info.Name);
			}
			fields.Sort(new PriorityComparer(t));

			return fields;
		}

		/// <summary>
		///     Get the TGIBlock list for a Content Field.
		/// </summary>
		/// <param name="o">The object to query.</param>
		/// <param name="f">The property name under inspection.</param>
		/// <returns>The TGIBlock list for a Content Field, if present; otherwise <c>null</c>.</returns>
		public static DependentList<TGIBlock> GetTGIBlocks(AApiVersionedFields o, string f)
		{
			string tgiBlockListCF = TGIBlockListContentFieldAttribute.GetTGIBlockListContentField(o.GetType(), f);
			if (tgiBlockListCF != null)
			{
				try
				{
					return o[tgiBlockListCF].Value as DependentList<TGIBlock>;
				}
				catch
				{
				}
			}
			return null;
		}

		/// <summary>
		///     Get the TGIBlock list for a Content Field.
		/// </summary>
		/// <param name="f">The property name under inspection.</param>
		/// <returns>The TGIBlock list for a Content Field, if present; otherwise <c>null</c>.</returns>
		public DependentList<TGIBlock> GetTGIBlocks(string f)
		{
			return GetTGIBlocks(this, f);
		}

		private class PriorityComparer : IComparer<string>
		{
			private readonly Type type;

			public PriorityComparer(Type t)
			{
				this.type = t;
			}

			public int Compare(string x, string y)
			{
				int res =
					ElementPriorityAttribute.GetPriority(this.type, x).CompareTo(ElementPriorityAttribute.GetPriority(this.type, y));
				if (res == 0)
				{
					res = string.Compare(x, y, StringComparison.Ordinal);
				}
				return res;
			}
		}

		private static readonly List<string> valueBuilderBanlist = new List<string>(new[] { "Value", "Stream", "AsBytes" });

		private static readonly List<string> dictionaryBanlist =
			new List<string>(new[] { "Keys", "Values", "Count", "IsReadOnly", "IsFixedSize", "IsSynchronized", "SyncRoot" });

		/// <summary>
		///     The fields ValueBuilder will return; used to eliminate those that should not be used.
		/// </summary>
		protected virtual List<string> ValueBuilderFields
		{
			get
			{
				List<string> fields = this.ContentFields;
				fields.RemoveAll(banlist.Contains);
				fields.RemoveAll(valueBuilderBanlist.Contains);
				if (this is IDictionary)
				{
					fields.RemoveAll(dictionaryBanlist.Contains);
				}
				return fields;
			}
		}

		/// <summary>
		///     Returns a string representing the value of the field (and any contained sub-fields)
		/// </summary>
		protected string ValueBuilder
		{
			get
			{
				StringBuilder sb = new StringBuilder();

				List<string> fields = this.ValueBuilderFields;

				const string headerFormat = "\n--- {0}: {1} (0x{2:X}) ---";

				foreach (string f in fields)
				{
					TypedValue tv = this[f];

					if (typeof(AApiVersionedFields).IsAssignableFrom(tv.Type))
					{
						AApiVersionedFields apiObj = tv.Value as AApiVersionedFields;
						if (apiObj.ContentFields.Contains("Value") &&
							typeof(string).IsAssignableFrom(
								GetContentFieldTypes(this.requestedApiVersion, tv.Type)["Value"]))
						{
							string elem = (string)apiObj["Value"].Value;
							if (elem.Contains("\n"))
							{
								sb.Append("\n--- " + tv.Type.Name + ": " + f + " ---\n   " + elem.Replace("\n", "\n   ").TrimEnd() + "\n---");
							}
							else
							{
								sb.Append("\n" + f + ": " + elem);
							}
						}
					}
					else if (tv.Type.BaseType != null && tv.Type.BaseType.Name.Contains("IndexList`"))
					{
						IList l = (IList)tv.Value;
						string fmt = "\n   [{0:X" + l.Count.ToString("X").Length + "}]: {1}";
						int i = 0;

						sb.Append(string.Format(headerFormat, tv.Type.Name, f, l.Count));
						foreach (AHandlerElement v in l)
						{
							sb.Append(string.Format(fmt, i++, (string)v["Value"].Value));
						}
						sb.Append("\n---");
					}
					else if (tv.Type.BaseType != null && tv.Type.BaseType.Name.Contains("SimpleList`"))
					{
						IList l = (IList)tv.Value;
						string fmt = "\n   [{0:X" + l.Count.ToString("X").Length + "}]: {1}";
						int i = 0;

						sb.Append(string.Format(headerFormat, tv.Type.Name, f, l.Count));
						foreach (AHandlerElement v in l)
						{
							sb.Append(string.Format(fmt, i++, v["Val"]));
						}
						sb.Append("\n---");
					}
					else if (typeof(DependentList<TGIBlock>).IsAssignableFrom(tv.Type))
					{
						DependentList<TGIBlock> l = (DependentList<TGIBlock>)tv.Value;
						string fmt = "\n   [{0:X" + l.Count.ToString("X").Length + "}]: {1}";
						int i = 0;

						sb.Append(string.Format(headerFormat, tv.Type.Name, f, l.Count));
						foreach (TGIBlock v in l)
						{
							sb.Append(string.Format(fmt, i++, v));
						}
						sb.Append("\n---");
					}
					else if (tv.Type.BaseType != null && tv.Type.BaseType.Name.Contains("DependentList`"))
					{
						IList l = (IList)tv.Value;
						string fmtLong = "\n--- {0}[{1:X" + l.Count.ToString("X").Length + "}] ---\n   ";
						string fmtShort = "\n   [{0:X" + l.Count.ToString("X").Length + "}]: {1}";
						int i = 0;

						sb.Append(string.Format(headerFormat, tv.Type.Name, f, l.Count));
						foreach (AHandlerElement v in l)
						{
							if (v.ContentFields.Contains("Value") &&
								typeof(string).IsAssignableFrom(
									GetContentFieldTypes(this.requestedApiVersion, v.GetType())["Value"]))
							{
								string elem = (string)v["Value"].Value;
								if (elem.Contains("\n"))
								{
									sb.Append(string.Format(fmtLong, f, i++) + elem.Replace("\n", "\n   ").TrimEnd());
								}
								else
								{
									sb.Append(string.Format(fmtShort, i++, elem));
								}
							}
						}
						sb.Append("\n---");
					}
					else if (tv.Type.HasElementType && typeof(AApiVersionedFields).IsAssignableFrom(tv.Type.GetElementType()))
					// it's an AApiVersionedFields array, slightly glossy...
					{
						sb.Append(string.Format(headerFormat, tv.Type.Name, f, ((Array)tv.Value).Length));
						sb.Append("\n   " + tv.ToString(CultureInfo.InvariantCulture).Replace("\n", "\n   ").TrimEnd() + "\n---");
					}
					else
					{
						string suffix = "";
						DependentList<TGIBlock> tgis = this.GetTGIBlocks(f);
						if (tgis != null && tgis.Count > 0)
						{
							try
							{
								int i = Convert.ToInt32(tv.Value);
								if (i >= 0 && i < tgis.Count)
								{
									TGIBlock tgi = tgis[i];
									suffix = " (" + tgi + ")";
								}
							}
							catch (Exception e)
							{
								sb.Append(" (Exception: " + e.Message + ")");
							}
						}
						sb.Append("\n" + f + ": " + tv + suffix);
					}
				}

			    IDictionary dict = this as IDictionary;
			    if (dict != null)
				{
				    string fmt = "\n   [{0:X" + dict.Count.ToString("X").Length + "}] {1}: {2}";
					int i = 0;
					sb.Append("\n--- (0x" + dict.Count.ToString("X") + ") ---");
					foreach (object key in dict.Keys)
					{
						sb.Append(string.Format(fmt,
												i++,
												new TypedValue(key.GetType(), key, "X"),
												new TypedValue(dict[key].GetType(), dict[key], "X")));
					}
					sb.Append("\n---");
				}

				return sb.ToString().Trim('\n');
			}
		}

		/// <summary>
		///     Returns a <see cref="System.String" /> that represents the current <see cref="AApiVersionedFields" /> object.
		/// </summary>
		/// <returns>A <see cref="System.String" /> that represents the current <see cref="AApiVersionedFields" /> object.</returns>
		public override string ToString()
		{
			return this.ValueBuilder;
		}

		/// <summary>
		///     Sorts Content Field names by their <see cref="ElementPriorityAttribute" /> (if set)
		/// </summary>
		/// <param name="x">First content field name</param>
		/// <param name="y">Second content field name</param>
		/// <returns>A signed number indicating the relative values of this instance and value.</returns>
		public int CompareByPriority(string x, string y)
		{
			return new PriorityComparer(this.GetType()).Compare(x, y);
		}

		/// <summary>
		///     Gets a lookup table from fieldname to type.
		/// </summary>
		/// <param name="APIversion">Version of API to use</param>
		/// <param name="t">API data type to query</param>
		/// <returns></returns>
		public static Dictionary<string, Type> GetContentFieldTypes(int APIversion, Type t)
		{
			Dictionary<string, Type> types = new Dictionary<string, Type>();

			int recommendedApiVersion = GetRecommendedApiVersion(t);
				//Could be zero if no "recommendedApiVersion" const field
			PropertyInfo[] ap = t.GetProperties();
			foreach (PropertyInfo m in ap)
			{
				if (banlist.Contains(m.Name))
				{
					continue;
				}
				if (!CheckVersion(t, m.Name, APIversion == 0 ? recommendedApiVersion : APIversion))
				{
					continue;
				}

				types.Add(m.Name, m.PropertyType);
			}

			return types;
		}

#if UNDEF

        protected static List<string> getMethods(Int32 APIversion, Type t)
        {
            List<string> methods = null;

            int recommendedApiVersion = GetRecommendedApiVersion(t);
            //Could be zero if no "recommendedApiVersion" const field
            methods = new List<string>();
            MethodInfo[] am = t.GetMethods();
            foreach (MethodInfo m in am)
            {
                if (!m.IsPublic || banlist.Contains(m.Name)) continue;
                if ((m.Name.StartsWith("get_", StringComparison.Ordinal) && m.GetParameters().Length == 0)) continue;
                if (!CheckVersion(t, m.Name, APIversion == 0 ? recommendedApiVersion : APIversion)) continue;

                methods.Add(m.Name);
            }

            return methods;
        }

        public List<string> Methods { get; }

        public TypedValue Invoke(string method, params TypedValue[] parms)
        {
            Type[] at = new Type[parms.Length];
            object[] ao = new object[parms.Length];
            for (int i = 0; i < parms.Length; i++)
            {
                at[i] = parms[i].Type;
                ao[i] = parms[i].Value;
            } //Array.Copy, please...

            MethodInfo m = this.GetType().GetMethod(method, at);
            if (m == null)
                throw new ArgumentOutOfRangeException("Unexpected method received: " + method + "(...)");

            return new TypedValue(m.ReturnType, m.Invoke(this, ao), "X");
        }
#endif

		/// <summary>
		///     A class enabling sorting API objects by a ContentFields name
		/// </summary>
		/// <typeparam name="T">API object type</typeparam>
		public class Comparer<T> : IComparer<T>
			where T : IContentFields
		{
			private readonly string field;

			/// <summary>
			///     Sort API Objects by <paramref name="field" />
			/// </summary>
			/// <param name="field">ContentField name to sort by</param>
			public Comparer(string field)
			{
				this.field = field;
			}

			#region IComparer<T> Members

			/// <summary>
			///     Compares two objects of type T and returns a value indicating whether one is less than,
			///     equal to, or greater than the other.
			/// </summary>
			/// <param name="x">The first IContentFields object to compare.</param>
			/// <param name="y">The second IContentFields object to compare.</param>
			/// <returns>
			///     Value Condition Less than zero -- x is less than y.
			///     Zero -- x equals y.
			///     Greater than zero -- x is greater than y.
			/// </returns>
			public int Compare(T x, T y)
			{
				return x[this.field].CompareTo(y[this.field]);
			}

			#endregion
		}

		// Random helper functions that should live somewhere...

		/// <summary>
		///     Convert a string (up to 8 characters) to a UInt64
		/// </summary>
		/// <param name="s">String to convert</param>
		/// <returns>UInt64 packed representation of <paramref name="s" /></returns>
		public static ulong FOURCC(string s)
		{
			if (s.Length > 8)
			{
				throw new ArgumentLengthException("String", 8);
			}
			ulong result = 0;
			for (int i = s.Length - 1; i >= 0; i--)
			{
				result += ((uint)s[i]) << (i * 8);
			}
			return result;
		}

		/// <summary>
		/// Convert a <see cref="ulong"/> to a string (up to 8 characters, high-order zeros omitted)
		/// </summary>
		/// <param name="value">The value to convert.</param>
		/// <returns>String representation of <paramref name="value" /></returns>
		public static string FOURCC(ulong value)
		{
			string result = "";
			for (int i = 7; i >= 0; i--)
			{
				char c = (char)((value >> (i * 8)) & 0xff);
				if (result.Length > 0 || c != 0)
				{
					result = c + result;
				}
			}
			return result;
		}

		/// <summary>
		///     Return a space-separated string containing valid enumeration names for the given type
		/// </summary>
		/// <param name="t">Enum type</param>
		/// <returns>Valid enum names</returns>
		public static string FlagNames(Type t)
		{
			string p = "";
			foreach (string q in Enum.GetNames(t))
			{
				p += " " + q;
			}
			return p.Trim();
		}
	}

	/// <summary>
	///     A useful extension to <see cref="AApiVersionedFields" /> where a change handler is required
	/// </summary>
	public abstract class AHandlerElement : AApiVersionedFields
	{
		/// <summary>
		///     Holds the <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.
		/// </summary>
		protected EventHandler handler;

		/// <summary>
		///     Indicates if the <see cref="AHandlerElement" /> has been changed by OnElementChanged()
		/// </summary>
		protected bool dirty;

		/// <summary>
		///     Initialize a new instance
		/// </summary>
		/// <param name="APIversion">The requested API version.</param>
		/// <param name="handler">The <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.</param>
		public AHandlerElement(int APIversion, EventHandler handler)
		{
			this.requestedApiVersion = APIversion;
			this.handler = handler;
		}

		/// <summary>
		///     Get a copy of the <see cref="AHandlerElement" /> but with a new change <see cref="EventHandler" />.
		/// </summary>
		/// <param name="handler">The replacement <see cref="EventHandler" /> delegate.</param>
		/// <returns>Return a copy of the <see cref="AHandlerElement" /> but with a new change <see cref="EventHandler" />.</returns>
		public virtual AHandlerElement Clone(EventHandler handler)
		{
			List<object> args = new List<object>(new object[] { this.requestedApiVersion, handler, this });

			// Default values for parameters are resolved by the compiler.
			// Activator.CreateInstance does not simulate this, so we have to do it.
			// Avoid writing a Binder class just for this...
			ConstructorInfo ci = this.GetType().GetConstructors()
						 .Where(c =>
								{
									ParameterInfo[] pi = c.GetParameters();

									// Our required arguments followed by one or more optional ones
									if (pi.Length <= args.Count)
									{
										return false;
									}
									if (pi[args.Count - 1].IsOptional)
									{
										return false;
									}
									if (!pi[args.Count].IsOptional)
									{
										return false;
									}

									// Do the required args match?
									for (int i = 0; i < args.Count; i++)
									{
										// null matches anything except a value type
										if (args[i] == null)
										{
											if (pi[i].ParameterType.IsValueType)
											{
												return false;
											}
										}
										else
										// Otherwise check the target parameter is assignable from the provided argument
											if (!pi[i].ParameterType.IsAssignableFrom(args[i].GetType()))
											{
												return false;
											}
									}

									// OK, we have a match

									// Pad the args with Type.Missing to save repeating the reflection
									for (int i = args.Count; i < pi.Length; i++)
									{
										args.Add(Type.Missing);
									}

									// Say we've found "the" match
									return true;
								})
				// Use the first one (or none...)
						 .FirstOrDefault();

			if (ci != null)
			{
				return ci.Invoke(args.ToArray()) as AHandlerElement;
			}

			return Activator.CreateInstance(this.GetType(), args.ToArray(), null) as AHandlerElement;
		}

		//public abstract AHandlerElement Clone(EventHandler handler);

		/// <summary>
		///     Flag the <see cref="AHandlerElement" /> as dirty and invoke the <see cref="EventHandler" /> delegate.
		/// </summary>
		protected virtual void OnElementChanged()
		{
			this.dirty = true;
			//Console.WriteLine(this.GetType().Name + " dirtied.");
			if (this.handler != null)
			{
				this.handler(this, EventArgs.Empty);
			}
		}

		/// <summary>
		///     Change the element change handler to <paramref name="handler" />.
		/// </summary>
		/// <param name="handler">The new element change handler.</param>
		internal void SetHandler(EventHandler handler)
		{
			if (this.handler != handler)
			{
				this.handler = handler;
			}
		}
	}

	/// <summary>
	///     An extension to <see cref="AHandlerElement" />, for simple data types (such as <see cref="UInt32" />).
	/// </summary>
	/// <typeparam name="T">A simple data type (such as <see cref="UInt32" />).</typeparam>
	/// <remarks>For an example of use, see <see cref="SimpleList{T}" />.</remarks>
	/// <seealso cref="SimpleList{T}" />
	public class HandlerElement<T> : AHandlerElement, IEquatable<HandlerElement<T>>
		where T : struct, IComparable, IConvertible, IEquatable<T>, IComparable<T>
	{
		private const int recommendedApiVersion = 1;
		private T val;

		/// <summary>
		///     Initialize a new instance with a default value.
		/// </summary>
		/// <param name="APIversion">The requested API version.</param>
		/// <param name="handler">The <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.</param>
		public HandlerElement(int APIversion, EventHandler handler) : this(APIversion, handler, default(T))
		{
		}

		/// <summary>
		///     Initialize a new instance with an initial value of <paramref name="basis" />.
		/// </summary>
		/// <param name="APIversion">The requested API version.</param>
		/// <param name="handler">The <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.</param>
		/// <param name="basis">Initial value for instance.</param>
		public HandlerElement(int APIversion, EventHandler handler, T basis)
			: base(APIversion, handler)
		{
			this.val = basis;
		}

		/// <summary>
		///     Initialize a new instance with an initial value from <paramref name="basis" />.
		/// </summary>
		/// <param name="APIversion">The requested API version.</param>
		/// <param name="handler">The <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.</param>
		/// <param name="basis">Element containing the initial value for instance.</param>
		public HandlerElement(int APIversion, EventHandler handler, HandlerElement<T> basis)
			: base(APIversion, handler)
		{
			this.val = basis.val;
		}

		#region AHandlerElement

		/// <summary>
		///     Get a copy of the HandlerElement but with a new change <see cref="EventHandler" />.
		/// </summary>
		/// <param name="handler">The replacement HandlerElement delegate.</param>
		/// <returns>Return a copy of the HandlerElement but with a new change <see cref="EventHandler" />.</returns>
		public override AHandlerElement Clone(EventHandler handler)
		{
			return new HandlerElement<T>(this.requestedApiVersion, handler, this.val);
		}

		/// <summary>
		///     The best supported version of the API available
		/// </summary>
		public override int RecommendedApiVersion
		{
			get { return recommendedApiVersion; }
		}

		/// <summary>
		///     The list of available field names on this API object.
		/// </summary>
		public override List<string> ContentFields
		{
			get { return GetContentFields(this.requestedApiVersion, this.GetType()); }
		}

		#endregion

		#region IEquatable<HandlerElement<T>>

		/// <summary>
		///     Indicates whether the current object is equal to another object of the same type.
		/// </summary>
		/// <param name="other">An object to compare with this object.</param>
		/// <returns>true if the current object is equal to the other parameter; otherwise, false.</returns>
		public bool Equals(HandlerElement<T> other)
		{
			return this.val.Equals(other.val);
		}

		/// <summary>
		///     Determines whether the specified <see cref="System.Object" /> is equal to the current
		///     <see cref="HandlerElement{T}" />.
		/// </summary>
		/// <param name="obj">The <see cref="System.Object" /> to compare with the current <see cref="HandlerElement{T}" />.</param>
		/// <returns>
		///     true if the specified <see cref="System.Object" /> is equal to the current <see cref="HandlerElement{T}" />;
		///     otherwise, false.
		/// </returns>
		/// <exception cref="System.NullReferenceException">The obj parameter is null.</exception>
		public override bool Equals(object obj)
		{
			if (obj is T)
			{
				return this.val.Equals((T)obj);
			}
			if (obj is HandlerElement<T>)
			{
				return this.Equals((HandlerElement<T>)obj);
			}
			return false;
		}

		/// <summary>
		///     Returns the hash code for this instance.
		/// </summary>
		/// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
		public override int GetHashCode()
		{
			return this.val.GetHashCode();
		}

		#endregion

		/// <summary>
		///     The value of the object.
		/// </summary>
		public T Val
		{
			get { return this.val; }
			set
			{
				if (!this.val.Equals(value))
				{
					this.val = value;
					this.OnElementChanged();
				}
			}
		}

		/// <summary>
		///     Implicit cast from <see cref="HandlerElement{T}" /> to <typeparamref name="T" />.
		/// </summary>
		/// <param name="value">Value to cast.</param>
		/// <returns>Cast value.</returns>
		public static implicit operator T(HandlerElement<T> value)
		{
			return value.val;
		}

		//// <summary>
		//// Implicit cast from <typeparamref name="T"/> to <see cref="HandlerElement{T}"/>.
		//// </summary>
		//// <param name="value">Value to cast.</param>
		//// <returns>Cast value.</returns>
		//--do not want to accidentally disrupt the content of lists through this cast!
		//public static implicit operator HandlerElement<T>(T value) { return new HandlerElement<T>(0, null, value); }

		/// <summary>
		///     Get displayable value.
		/// </summary>
		public string Value
		{
			get { return new TypedValue(typeof(T), this.val, "X").ToString(CultureInfo.InvariantCulture); }
		}
	}

	/// <summary>
	///     An extension to <see cref="AHandlerElement" />, for lists of TGIBlockList indices.
	/// </summary>
	/// <typeparam name="T">A simple data type (such as <see cref="Int32" />).</typeparam>
	/// <remarks>For an example of use, see <see cref="IndexList{T}" />.</remarks>
	/// <seealso cref="IndexList{T}" />
	public class TGIBlockListIndex<T> : AHandlerElement, IEquatable<TGIBlockListIndex<T>>
		where T : struct, IComparable, IConvertible, IEquatable<T>, IComparable<T>
	{
		private const int recommendedApiVersion = 1;

		/// <summary>
		///     Reference to list into which this is an index.
		/// </summary>
		public DependentList<TGIBlock> ParentTGIBlocks { get; set; }

		private T data;

		/// <summary>
		///     Initialize a new instance with a default value.
		/// </summary>
		/// <param name="APIversion">The requested API version.</param>
		/// <param name="handler">The <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.</param>
		/// <param name="ParentTGIBlocks">Reference to list into which this is an index.</param>
		public TGIBlockListIndex(int APIversion, EventHandler handler, DependentList<TGIBlock> ParentTGIBlocks = null)
			: this(APIversion, handler, default(T), ParentTGIBlocks)
		{
		}

		/// <summary>
		///     Initialize a new instance with an initial value from <paramref name="basis" />.
		/// </summary>
		/// <param name="APIversion">The requested API version.</param>
		/// <param name="handler">The <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.</param>
		/// <param name="basis">Element containing the initial value for instance.</param>
		/// <param name="ParentTGIBlocks">
		///     Reference to list into which this is an index, or null to use that in
		///     <paramref name="basis" />.
		/// </param>
		public TGIBlockListIndex(int APIversion,
								 EventHandler handler,
								 TGIBlockListIndex<T> basis,
								 DependentList<TGIBlock> ParentTGIBlocks = null)
			: this(APIversion, handler, basis.data, ParentTGIBlocks ?? basis.ParentTGIBlocks)
		{
		}

		/// <summary>
		///     Initialize a new instance with an initial value of <paramref name="value" />.
		/// </summary>
		/// <param name="APIversion">The requested API version.</param>
		/// <param name="handler">The <see cref="EventHandler" /> delegate to invoke if the <see cref="AHandlerElement" /> changes.</param>
		/// <param name="value">Initial value for instance.</param>
		/// <param name="ParentTGIBlocks">Reference to list into which this is an index.</param>
		public TGIBlockListIndex(int APIversion, EventHandler handler, T value, DependentList<TGIBlock> ParentTGIBlocks = null)
			: base(APIversion, handler)
		{
			this.ParentTGIBlocks = ParentTGIBlocks;
			this.data = value;
		}

		#region AHandlerElement

		// /// <summary>
		// /// Get a copy of the HandlerElement but with a new change <see cref="EventHandler"/>.
		// /// </summary>
		// /// <param name="handler">The replacement HandlerElement delegate.</param>
		// /// <returns>Return a copy of the HandlerElement but with a new change <see cref="EventHandler"/>.</returns>
		// public override AHandlerElement Clone(EventHandler handler) { return new TGIBlockListIndex<T>(requestedApiVersion, handler, data) { ParentTGIBlocks = ParentTGIBlocks }; }

		/// <summary>
		///     The best supported version of the API available
		/// </summary>
		public override int RecommendedApiVersion
		{
			get { return recommendedApiVersion; }
		}

		/// <summary>
		///     The list of available field names on this API object.
		/// </summary>
		public override List<string> ContentFields
		{
			get
			{
				List<string> res = GetContentFields(this.requestedApiVersion, this.GetType());
				res.Remove("ParentTGIBlocks");
				return res;
			}
		}

		#endregion

		#region IEquatable<TGIBlockListIndex<T>>

		/// <summary>
		///     Indicates whether the current object is equal to another object of the same type.
		/// </summary>
		/// <param name="other">An object to compare with this object.</param>
		/// <returns>true if the current object is equal to the other parameter; otherwise, false.</returns>
		public bool Equals(TGIBlockListIndex<T> other)
		{
			return this.data.Equals(other.data);
		}

		/// <summary>
		///     Determines whether the specified <see cref="System.Object" /> is equal to the current
		///     <see cref="HandlerElement{T}" />.
		/// </summary>
		/// <param name="obj">The <see cref="System.Object" /> to compare with the current <see cref="HandlerElement{T}" />.</param>
		/// <returns>
		///     true if the specified <see cref="System.Object" /> is equal to the current <see cref="HandlerElement{T}" />;
		///     otherwise, false.
		/// </returns>
		/// <exception cref="System.NullReferenceException">The obj parameter is null.</exception>
		public override bool Equals(object obj)
		{
			if (obj is T)
			{
				return this.data.Equals((T)obj);
			}
			if (obj is TGIBlockListIndex<T>)
			{
				return this.Equals(obj as TGIBlockListIndex<T>);
			}
			return false;
		}

		/// <summary>
		///     Returns the hash code for this instance.
		/// </summary>
		/// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
		public override int GetHashCode()
		{
			return this.data.GetHashCode();
		}

		#endregion

		/// <summary>
		///     The value of the object.
		/// </summary>
		[TGIBlockListContentField("ParentTGIBlocks")]
		public T Data
		{
			get { return this.data; }
			set
			{
				if (!this.data.Equals(value))
				{
					this.data = value;
					this.OnElementChanged();
				}
			}
		}

		/// <summary>
		///     Implicit cast from <see cref="HandlerElement{T}" /> to <typeparamref name="T" />.
		/// </summary>
		/// <param name="value">Value to cast.</param>
		/// <returns>Cast value.</returns>
		public static implicit operator T(TGIBlockListIndex<T> value)
		{
			return value.data;
		}

		//// <summary>
		//// Implicit cast from <typeparamref name="T"/> to <see cref="HandlerElement{T}"/>.
		//// </summary>
		//// <param name="value">Value to cast.</param>
		//// <returns>Cast value.</returns>
		//--do not want to accidentally disrupt the content of lists through this cast!
		//public static implicit operator HandlerElement<T>(T value) { return new HandlerElement<T>(0, null, value); }
		/// <summary>
		///     Displayable value
		/// </summary>
		public string Value
		{
			get { return this.ValueBuilder.Replace("Data: ", ""); }
		}
	}
}
