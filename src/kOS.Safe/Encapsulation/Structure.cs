﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Exceptions;
using kOS.Safe.Utilities;

namespace kOS.Safe.Encapsulation
{
    public abstract class Structure : ISuffixed, IOperable 
    {
        private static readonly IDictionary<Type,IDictionary<string, ISuffix>> globalSuffixes;
        private readonly IDictionary<string, ISuffix> instanceSuffixes;
        private static readonly object globalSuffixLock = new object();

        static Structure()
        {
            globalSuffixes = new Dictionary<Type, IDictionary<string, ISuffix>>();
            
        }

        protected Structure()
        {
            instanceSuffixes = new Dictionary<string, ISuffix>(StringComparer.OrdinalIgnoreCase);
            InitializeInstanceSuffixes();
        }


        private void InitializeInstanceSuffixes()
        {
              // Need to choose what sort of naming scheme to return before
              // enabling this one:
              //     AddSuffix("TYPENAME",   new NoArgsSuffix<StringValue>(() => GetType().ToString()));

              AddSuffix("TOSTRING",       new NoArgsSuffix<StringValue>(() => ToString()));
              AddSuffix("HASSUFFIX",      new OneArgsSuffix<BooleanValue, StringValue>(HasSuffix));
              AddSuffix("SUFFIXNAMES",    new NoArgsSuffix<ListValue<StringValue>>(GetSuffixNames));
              AddSuffix("ISSERIALIZABLE", new NoArgsSuffix<BooleanValue>(() => this is SerializableStructure));
        }

        protected void AddSuffix(string suffixName, ISuffix suffixToAdd)
        {
            AddSuffix(new[]{suffixName}, suffixToAdd);
        }

        protected void AddSuffix(IEnumerable<string> suffixNames, ISuffix suffixToAdd)
        {
            foreach (var suffixName in suffixNames)
            {
                if (instanceSuffixes.ContainsKey(suffixName))
                {
                    instanceSuffixes[suffixName] = suffixToAdd;
                }
                else
                {
                    instanceSuffixes.Add(suffixName, suffixToAdd);
                }
            }
        }

        protected static void AddGlobalSuffix<T>(string suffixName, ISuffix suffixToAdd)
        {
            AddGlobalSuffix<T>(new[]{suffixName}, suffixToAdd);
        }

        protected static void AddGlobalSuffix<T>(IEnumerable<string> suffixNames, ISuffix suffixToAdd)
        {
            var type = typeof (T);
            var typeSuffixes = GetStaticSuffixesForType(type);

            foreach (var suffixName in suffixNames)
            {
                if (typeSuffixes.ContainsKey(suffixName))
                {
                    typeSuffixes[suffixName] = suffixToAdd;
                }
                else
                {
                    typeSuffixes.Add(suffixName, suffixToAdd);
                }
            }
            lock (globalSuffixLock)
            {
                globalSuffixes[type] = typeSuffixes;
            }
        }

        private static IDictionary<string, ISuffix> GetStaticSuffixesForType(Type currentType)
        {
            lock (globalSuffixLock)
            {
                IDictionary<string, ISuffix> typeSuffixes;
                if (globalSuffixes.TryGetValue(currentType, out typeSuffixes))
                {
                    return typeSuffixes;
                }
                return new Dictionary<string, ISuffix>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public virtual bool SetSuffix(string suffixName, object value)
        {
            var suffixes = GetStaticSuffixesForType(GetType());

            if (!ProcessSetSuffix(suffixes, suffixName, value))
            {
                return ProcessSetSuffix(instanceSuffixes, suffixName, value);
            }
            return false;
        }

        private bool ProcessSetSuffix(IDictionary<string, ISuffix> suffixes, string suffixName, object value)
        {
            ISuffix suffix;
            if (suffixes.TryGetValue(suffixName, out suffix))
            {
                var settable = suffix as ISetSuffix;
                if (settable != null)
                {
                    settable.Set(value);
                    return true;
                }
                throw new KOSSuffixUseException("set", suffixName, this);
            }
            return false;
        }

        public virtual ISuffixResult GetSuffix(string suffixName)
        {
            ISuffix suffix;
            if (instanceSuffixes.TryGetValue(suffixName, out suffix))
            {
                return suffix.Get();
            }

            var suffixes = GetStaticSuffixesForType(GetType());

            if (!suffixes.TryGetValue(suffixName, out suffix))
            {
                throw new KOSSuffixUseException("get",suffixName,this);
            }
            return suffix.Get();
        }
        
        public virtual BooleanValue HasSuffix(StringValue suffixName)
        {
            if (instanceSuffixes.ContainsKey(suffixName.ToString()))
                return true;
            if (GetStaticSuffixesForType(GetType()).ContainsKey(suffixName.ToString()))
                return true;
            return false;
        }
        
        public virtual ListValue<StringValue> GetSuffixNames()
        {
            List<StringValue> names = new List<StringValue>();            
            
            names.AddRange(instanceSuffixes.Keys.Select(item => (StringValue)item));
            names.AddRange(GetStaticSuffixesForType(GetType()).Keys.Select(item => (StringValue)item));
            
            // Return the list alphabetized by suffix name.  The key lookups above, since they're coming
            // from a hashed dictionary, won't be in any predictable ordering:
            return new ListValue<StringValue>(names.OrderBy(item => item.ToString()));
        }

        public virtual object TryOperation(string op, object other, bool reverseOrder)
        {
            if (op == "==")
            {
                return Equals(other);
            }
            if (op == "<>")
            {
                return !Equals(other);
            }
            if (op == "+")
            {
                return ToString() + other;
            }

            var message = string.Format("Cannot perform the operation: {0} On Structures {1} and {2}", op, GetType(),
                other.GetType());
            SafeHouse.Logger.Log(message);
            throw new InvalidOperationException(message);
        }

        protected object ConvertToDoubleIfNeeded(object value)
        {
            if (!(value is Structure) && !(value is double))
            {
                value = Convert.ToDouble(value);
            }

            return value;
        }

        public override string ToString()
        {
            return "Structure ";
        }

        public static StringValue operator +(Structure val1, Structure val2)
        {
            return new StringValue(string.Concat(val1, val2));
        }

        /// <summary>
        /// Attempt to convert the given object into a kOS encapsulation type (something
        /// derived from kOS.Safe.Encapsulation.Structure), returning that instead.
        /// This never throws exception or complains in any way if the conversion cannot happen.
        /// Insted in that case it just silently ignores the request and returns the original object
        /// reference unchanged.  Thus it is safe to call it "just in case", even in places where it won't
        /// always be necessary, or have an effect at all.  You should use in anywhere you need to
        /// ensure that a value a user's script might see on the stack or in a script variable is properly
        /// wrapped in a kOS Structure, and not just a raw primitive like int or double.
        /// </summary>
        /// <param name="value">value to convert</param>
        /// <returns>new converted value, or original value if conversion couldn't happen or was unnecesary</returns>
        public static object FromPrimitive(object value)
        {
            if (value == null)
                return value; // If a null exists, let it pass through so it will bomb elsewhere, not here in FromPrimitive() where the exception message would be obtuse.
            
            if (value is Structure)
                return value; // Conversion is unnecessary - it's already a Structure.
            
            var convert = value as IConvertible;
            if (convert == null)
                return value; // Conversion isn't even theoretically possible.
            
            TypeCode code = convert.GetTypeCode();
            switch (code)
            {
                case TypeCode.Boolean:
                    return new BooleanValue(Convert.ToBoolean(convert));
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return ScalarValue.Create(Convert.ToDouble(convert));
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return ScalarValue.Create(Convert.ToInt32(convert));
                case TypeCode.String:
                    return new StringValue(Convert.ToString(convert, CultureInfo.CurrentCulture));
                default:
                    break;
            }
            return value; // Conversion is one this method didn't implement.
        }
        
        /// <summary>
        /// This is identical to FromPrimitive, except that it WILL throw an exception
        /// if it was unable to guarantee that the result became (or already was) a kOS Structure.
        /// </summary>
        /// <param name="value">value to convert</param>
        /// <returns>value after conversion, or original value if conversion unnecessary</returns>
        public static Structure FromPrimitiveWithAssert(object value)
        {
            object convertedVal = FromPrimitive(value);
            Structure returnValue = convertedVal as Structure;
            if (returnValue == null)
                throw new KOSException(
                    String.Format("Internal Error.  Contact the kOS developers with the phrase 'impossible FromPrimitiveWithAssert({0}) was attempted'.\nAlso include the output log if you can.",
                                  (value == null ? "<null>" : value.GetType().ToString())));
            return returnValue;
        }

        public static object ToPrimitive(object value)
        {
            var scalarValue = value as ScalarValue;
            if (scalarValue != null)
            {
                return scalarValue.Value;
            }
            var booleanValue = value as BooleanValue;
            if (booleanValue != null)
            {
                return booleanValue.Value;
            }
            var stringValue = value as StringValue;
            if (stringValue != null)
            {
                return stringValue.ToString();
            }

            return value;
        }
    }
}
