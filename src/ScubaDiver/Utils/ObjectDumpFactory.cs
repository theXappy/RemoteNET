using ScubaDiver.API.Dumps;
using ScubaDiver.API.Utils;
using ScubaDiver.API.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ScubaDiver.Utils
{
    public static class ObjectDumpFactory
    {
        public static ObjectDump Create(object instance, ulong retrievalAddr, ulong pinAddr)
        {
            Type dumpedObjType = instance.GetType();
            ObjectDump od;
            if (dumpedObjType.IsPrimitiveEtc() || instance is IEnumerable)
            {
                od = new ObjectDump()
                {
                    RetrivalAddress = retrievalAddr,
                    PinnedAddress = pinAddr,
                    PrimitiveValue = PrimitivesEncoder.Encode(instance),
                    HashCode = instance.GetHashCode()
                };
            }
            else
            {
                List<MemberDump> fields = new List<MemberDump>();
                foreach (var fieldInfo in dumpedObjType.GetFields((BindingFlags)0xffff))
                {
                    try
                    {
                        var fieldValue = fieldInfo.GetValue(instance);
                        bool hasEncValue = false;
                        string encValue = null;
                        if (fieldValue.GetType().IsPrimitiveEtc() || fieldValue is IEnumerable)
                        {
                            hasEncValue = true;
                            encValue = PrimitivesEncoder.Encode(fieldValue);
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
                        var propValue = propInfo.GetValue(instance);
                        bool hasEncValue = false;
                        string encValue = null;
                        if (propValue.GetType().IsPrimitiveEtc() || propValue is IEnumerable)
                        {
                            hasEncValue = true;
                            encValue = PrimitivesEncoder.Encode(propValue);
                        }

                        props.Add(new MemberDump()
                        {
                            Name = propInfo.Name,
                            HasEncodedValue = hasEncValue,
                            EncodedValue = encValue
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

                od = new ObjectDump()
                {
                    RetrivalAddress = retrievalAddr,
                    PinnedAddress = pinAddr,
                    Type = dumpedObjType.ToString(),
                    Fields = fields,
                    Properties = props,
                    HashCode = instance.GetHashCode()
                };
            }

            return od;
        }
    }
}
