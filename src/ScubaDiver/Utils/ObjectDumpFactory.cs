using ScubaDiver.API.Dumps;
using ScubaDiver.API.Utils;
using ScubaDiver.API.Extensions;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace ScubaDiver.Utils
{
    public static class ObjectDumpFactory
    {
        public static ObjectDump Create(object instance, ulong retrievalAddr, ulong pinAddr)
        {
            Type dumpedObjType = instance.GetType();
            ObjectDump od;
            if (dumpedObjType.IsPrimitiveEtc())
            {
                od = new ObjectDump()
                {
                    Type = instance.GetType().ToString(),
                    RetrivalAddress = retrievalAddr,
                    PinnedAddress = pinAddr,
                    PrimitiveValue = PrimitivesEncoder.Encode(instance),
                    HashCode = instance.GetHashCode()
                };
                return od;
            }
            else if (instance is Array enumerable)
            {
                Type elementsType = instance.GetType().GetElementType();

                if (elementsType.IsPrimitiveEtc())
                {
                    // Collection of primitives can be encoded using the PrimitivesEncoder
                    od = new ObjectDump()
                    {
                        ObjectType = ObjectType.Array,
                        SubObjectsType = ObjectType.Primitive,
                        RetrivalAddress = retrievalAddr,
                        PinnedAddress = pinAddr,
                        PrimitiveValue = PrimitivesEncoder.Encode(instance),
                        SubObjectsCount = enumerable.Length,
                        Type = dumpedObjType.FullName,
                        HashCode = instance.GetHashCode()
                    };
                    return od;
                }
                else
                {
                    // It's an array of objects. We need to treat it in a unique way.
                    od = new ObjectDump()
                    {
                        ObjectType = ObjectType.Array,
                        SubObjectsType = ObjectType.NonPrimitive,
                        RetrivalAddress = retrievalAddr,
                        PinnedAddress = pinAddr,
                        PrimitiveValue = "==UNUSED==",
                        SubObjectsCount = enumerable.Length,
                        Type = dumpedObjType.FullName,
                        HashCode = instance.GetHashCode()
                    };

                    dumpedObjType = typeof(Array);
                    // Falling out of the `if` to go fill with all fields and such...
                }
            }
            else
            {
                // General non-array or primitive object
                od = new ObjectDump()
                {
                    ObjectType = ObjectType.NonPrimitive,
                    RetrivalAddress = retrievalAddr,
                    PinnedAddress = pinAddr,
                    Type = dumpedObjType.FullName,
                    HashCode = instance.GetHashCode()
                };
            }




            List<MemberDump> fields = new List<MemberDump>();
            var eventNames = dumpedObjType.GetEvents((BindingFlags)0xffff).Select(eventInfo => eventInfo.Name);
            foreach (var fieldInfo in dumpedObjType.GetFields((BindingFlags)0xffff).Where(fieldInfo => !eventNames.Contains(fieldInfo.Name)))
            {
                try
                {
                    var fieldValue = fieldInfo.GetValue(instance);
                    bool hasEncValue = false;
                    string encValue = null;
                    if (fieldValue != null)
                    {
                        hasEncValue = PrimitivesEncoder.TryEncode(fieldValue, out encValue);
                    }

                    fields.Add(new MemberDump()
                    {
                        Name = fieldInfo.Name,
                        HasEncodedValue = hasEncValue,
                        EncodedValue = encValue
                    });
                }
                catch (Exception e)
                {
                    fields.Add(new MemberDump()
                    {
                        Name = fieldInfo.Name,
                        HasEncodedValue = false,
                        RetrivalError = $"Failed to read. Exception: {e}"
                    });
                }
            }

            List<MemberDump> props = new List<MemberDump>();
            foreach (var propInfo in dumpedObjType.GetProperties((BindingFlags)0xffff))
            {
                if (propInfo.GetMethod == null)
                {
                    // No getter, skipping
                    continue;
                }

                try
                {
                    //
                    // Property dumping is disabled. It should be accessed on demend using th 'get_' function.
                    //
                    
                    //var propValue = propInfo.GetValue(instance);
                    //bool hasEncValue = false;
                    //string encValue = null;
                    //if (propValue.GetType().IsPrimitiveEtc() || propValue is IEnumerable)
                    //{
                    //    hasEncValue = true;
                    //    encValue = PrimitivesEncoder.Encode(propValue);
                    //}

                    props.Add(new MemberDump()
                    {
                        Name = propInfo.Name,
                        HasEncodedValue = false,
                        EncodedValue = null,
                    });
                }
                catch (Exception e)
                {
                    props.Add(new MemberDump()
                    {
                        Name = propInfo.Name,
                        HasEncodedValue = false,
                        RetrivalError = $"Failed to read. Exception: {e}"
                    });
                }
            }


            // populate fields and properties
            od.Fields = fields;
            od.Properties = props;


            return od;
        }
}
}
