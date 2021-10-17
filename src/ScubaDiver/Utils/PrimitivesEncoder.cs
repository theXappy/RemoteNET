﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ScubaDiver.Extensions;

namespace ScubaDiver.Utils
{
    public static class PrimitivesEncoder
    {
        /// <summary>
        /// Encodes a primitive or array of primitives 
        /// </summary>
        /// <param name="toEncode">Object or array to encode</param>
        /// <returns>Encoded value as a string</returns>
        public static string Encode(object toEncode)
        {
            if (toEncode.GetType().IsPrimitiveEtc())
            {
                // These types can just be ".Parse()"-ed back
                return toEncode.ToString();
            }

            if (toEncode is Array enumerable)
            {
                Console.WriteLine($"[Diver][PrimEnc] Trying to encode an array: {toEncode}");
                object[] objectsEnumerable = enumerable.Cast<object>().ToArray();
                Type elementsType = toEncode.GetType().GetElementType();
                Console.WriteLine($"[Diver][PrimEnc] Trying to encode an array: {toEncode}. ElementType: {elementsType}");
                if (!elementsType.IsPrimitiveEtc())
                {
                    throw new Exception("At least one element in the array is not primitive");
                }

                // Otherwise - this is an array of primitives.
                string output = String.Empty;
                foreach (object o in objectsEnumerable)
                {
                    string currObjectValue = Encode(o);
                    // Escape commas
                    currObjectValue = currObjectValue.Replace(",", "\\,");
                    if (output != String.Empty)
                    {
                        output += ",";
                    }

                    output += $"\"{currObjectValue}\"";
                }

                return output;
            }

            throw new ArgumentException(
                $"Object to encode was not a primitive or an array. TypeFullName: {toEncode.GetType()}");
        }


        public static object Decode(string toDecode, Type resultType)
        {
            // Easiest case - strings are encoded to themselves
            if (resultType == typeof(string))
                return toDecode;

            if (resultType.IsPrimitiveEtc())
            {
                var parseMethod = resultType.GetMethod("Parse", new Type[1] {typeof(string)});
                return parseMethod.Invoke(null, new object[] { toDecode });
            }

            if (resultType.IsArray)
            {
                List<int> commas = new List<int>();
                for (int i = 1; i < toDecode.Length; i++)
                {
                    if (toDecode[i] == ',' && toDecode[i - 1] != '\\')
                    {
                        commas.Add(i);
                    }
                }

                List<string> encodedElements = new List<string>();
                for (int i = 0; i < commas.Count; i++)
                {
                    int currCommaIndex = commas[i];
                    int nextCommandIndex = toDecode.Length;
                    if (i != commas.Count - 1)
                    {
                        nextCommandIndex = commas[i + 1];
                    }
                    encodedElements.Add(toDecode.Substring(currCommaIndex + 1, nextCommandIndex-currCommaIndex -1).Trim('\"'));
                }

                Type elementType = resultType.GetElementType();
                List<object> decodedObjects = new List<object>();
                foreach (string encodedElement in encodedElements)
                {
                    var unescapedEncElement = encodedElement.Replace("\\,", ",");
                    object decodedObject = Decode(unescapedEncElement, elementType);
                    decodedObjects.Add(decodedObject);
                }

                // Create a runtime array of the specific element type then copying from the list to it
                Array arr = Array.CreateInstance(elementType, decodedObjects.Count);
                for (int i = 0; i < decodedObjects.Count; i++)
                {
                    arr.SetValue(decodedObjects[i], i);
                }

                return arr;
            }

            
            throw new ArgumentException(
                $"Result type was not a primitive or an array. TypeFullName: {resultType}");
        }

        public static object Decode(string toDecode, string fullTypeName)
        {
            Type t = AppDomain.CurrentDomain.GetType(fullTypeName);
            if(t != null)
                return Decode(toDecode, t);

            throw new Exception($"Could not resolve type name \"{fullTypeName}\" in the current AppDomain");
        }
    }
}
