using System;
using System.Collections.Generic;
using System.Linq;
using ScubaDiver.API.Extensions;

namespace ScubaDiver.API.Utils
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

            if (!(toEncode is Array enumerable))
            {
                throw new ArgumentException(
                    $"Object to encode was not a primitive or an array. TypeFullName: {toEncode.GetType()}");
            }

            if (!toEncode.GetType().IsPrimitiveEtcArray())
            {
                // TODO: Support arrays of RemoteObjects/DynamicRemoteObject
                throw new Exception("At least one element in the array is not primitive");
            }

            // Otherwise - this is an array of primitives.
            string output = string.Empty;
            object[] objectsEnumerable = enumerable.Cast<object>().ToArray();
            foreach (object o in objectsEnumerable)
            {
                string currObjectValue = Encode(o);
                // Escape commas
                currObjectValue = currObjectValue.Replace(",", "\\,");
                if (output != string.Empty)
                {
                    output += ",";
                }

                output += $"\"{currObjectValue}\"";
            }

            return output;
        }

        public static bool TryEncode(object toEncode, out string res)
        {
            res = default(string);
            if (!(toEncode.GetType().IsPrimitiveEtc()))
            {
                if (toEncode is Array)
                {
                    Type elementsType = toEncode.GetType().GetElementType();
                    if (!elementsType.IsPrimitiveEtc())
                    {
                        // Array of non-primitives --> not primitive
                        return true;
                    }
                    else
                    {
                        // It's a primitives array, All go exit if clauses and encode
                    }
                }
                else
                {
                    // Not primitive ETC nor array --> not primitive
                    return false;
                }
            }

            // All good, can encode with no exceptions:
            res = Encode(toEncode);
            return true;
        }


        public static object Decode(ObjectOrRemoteAddress oora)
        {
            if (oora.IsRemoteAddress)
                throw new ArgumentException("Can not decode ObjectOrRemoteAddress object which represents a remote address.");
            return Decode(oora.EncodedObject, oora.Type);
        }
        public static object Decode(string toDecode, Type resultType)
        {
            // Easiest case - strings are encoded to themselves
            if (resultType == typeof(string))
                return toDecode;

            if (resultType.IsPrimitiveEtc())
            {
                var parseMethod = resultType.GetMethod("Parse", new Type[1] { typeof(string) });
                return parseMethod.Invoke(null, new object[] { toDecode });
            }

            if (resultType.IsArray)
            {
                List<int> commas = new();
                commas.Add(0); // To capture the first item we need to "imagine a comma" right before it.
                for (int i = 1; i < toDecode.Length; i++)
                {
                    if (toDecode[i] == ',' && toDecode[i - 1] != '\\')
                    {
                        commas.Add(i);
                    }
                }

                List<string> encodedElements = new();
                for (int i = 0; i < commas.Count; i++)
                {
                    int currCommaIndex = commas[i];
                    int nextCommandIndex = toDecode.Length;
                    if (i != commas.Count - 1)
                    {
                        nextCommandIndex = commas[i + 1];
                    }
                    encodedElements.Add(toDecode.Substring(currCommaIndex + 1, nextCommandIndex - currCommaIndex - 1).Trim('\"'));
                }

                Type elementType = resultType.GetElementType();
                List<object> decodedObjects = new();
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
            // NOTE: I'm allowing this decode to be estricted to the current domain (Instead of searching in all domains)
            // because I want to believe only primitive types will be handed here and those
            // should all be available in all domains. (hopefully)
            Type t = AppDomain.CurrentDomain.GetType(fullTypeName);
            if (t != null)
                return Decode(toDecode, t);

            throw new Exception($"Could not resolve type name \"{fullTypeName}\" in the current AppDomain");
        }
    }
}
