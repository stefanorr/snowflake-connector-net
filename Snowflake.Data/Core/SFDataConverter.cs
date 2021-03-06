﻿/*
 * Copyright (c) 2012-2017 Snowflake Computing Inc. All rights reserved.
 */

using System;
using System.ComponentModel;
using System.Data;
using System.Text;
using Common.Logging;
using Snowflake.Data.Client;

namespace Snowflake.Data.Core
{
    public enum SFDataType
    {
        None, FIXED, REAL, TEXT, DATE, VARIANT, TIMESTAMP_LTZ, TIMESTAMP_NTZ,
        TIMESTAMP_TZ, OBJECT, BINARY, TIME, BOOLEAN
    }

    class SFDataConverter
    {
        static private ILog logger = LogManager.GetLogger<SFDataConverter>();

        static private DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static internal Object convertToCSharpVal(string srcVal, SFDataType srcType, Type destType)
        {
            logger.TraceFormat("src value: {0}, srcType: {1}, destType: {2}", srcVal, srcType, destType);
            if (srcVal == null) return srcVal;
            
            if (destType == typeof(Int16) 
                || destType == typeof(Int32)
                || destType == typeof(Int64)
                || destType == typeof(Guid)
                || destType == typeof(Double)
                || destType == typeof(float)
                || destType == typeof(Decimal))
            {
                var typeConverter = TypeDescriptor.GetConverter(destType);
                return typeConverter.ConvertFrom(srcVal);
            }
            else if (destType == typeof(Boolean))
            {
                return String.Compare(srcVal, "1", true) == 0 
                    || String.Compare(srcVal, "true", true) == 0;
            }
            else if (destType == typeof(string))
            {
                return srcVal;
            }
            else if (destType == typeof(DateTime))
            {
                return convertToDateTime(srcVal, srcType);
            }
            else if (destType == typeof(DateTimeOffset))
            {
                return convertToDateTimeOffset(srcVal, srcType);
            }
            else if (destType == typeof(byte[]))
            {
                return srcType == SFDataType.BINARY ? 
                    hexToBytes(srcVal) : Encoding.UTF8.GetBytes(srcVal);
            }
            else
            {
                throw new SnowflakeDbException(SFError.INTERNAL_ERROR, "Invalid destination type.");
            }
        }

        static private DateTime convertToDateTime(string srcVal, SFDataType srcType)
        {
            switch (srcType)
            {
                case SFDataType.DATE:
                    long srcValLong = Int64.Parse(srcVal);
                    return unixEpoch.AddDays(srcValLong);

                case SFDataType.TIME:
                case SFDataType.TIMESTAMP_NTZ:

                    Tuple<long, long> secAndNsec = extractTimestamp(srcVal);
                    return unixEpoch.AddTicks((long)(secAndNsec.Item1 * 1000 * 1000 * 1000 + secAndNsec.Item2) / 100);

                default:
                    throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, srcType, typeof(DateTime));
            }
        }

        static private DateTimeOffset convertToDateTimeOffset(string srcVal, SFDataType srcType)
        {
            switch (srcType)
            {
                case SFDataType.TIMESTAMP_TZ:
                    int spaceIndex = srcVal.IndexOf(' ');
                    if (spaceIndex == -1)
                    {
                        throw new SnowflakeDbException(SFError.INTERNAL_ERROR, 
                            String.Format("Invalid timestamp_tz value: {0}", srcVal));
                    }
                    else
                    {
                        Tuple<long, long> secAndNsecTZ = extractTimestamp(srcVal.Substring(0, spaceIndex));

                        int offset = Int32.Parse(srcVal.Substring(spaceIndex + 1, srcVal.Length - spaceIndex - 1));
                        TimeSpan offSetTimespan = new TimeSpan((offset - 1440) / 60, 0, 0);
                        return new DateTimeOffset(unixEpoch.Ticks +
                            (secAndNsecTZ.Item1 * 1000 * 1000 * 1000 + secAndNsecTZ.Item2) / 100, TimeSpan.Zero).ToOffset(offSetTimespan);
                    }
                case SFDataType.TIMESTAMP_LTZ:
                    Tuple<long, long> secAndNsecLTZ = extractTimestamp(srcVal);
                      
                    return new DateTimeOffset(unixEpoch.Ticks +
                        (secAndNsecLTZ.Item1 * 1000 * 1000 * 1000 + secAndNsecLTZ.Item2) / 100, TimeSpan.Zero).ToLocalTime(); 

                default:
                    throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, 
                        srcType, typeof(DateTimeOffset).ToString());
            }
        }

        private static Tuple<long, long> extractTimestamp(string srcVal)
        {
            int dotIndex = srcVal.IndexOf('.');
            if (dotIndex == -1)
            {
                return Tuple.Create(Int64.Parse(srcVal), (long)0);
            }
            else
            {
                return Tuple.Create(Int64.Parse(srcVal.Substring(0, dotIndex)), 
                    Int64.Parse(srcVal.Substring(dotIndex+1, srcVal.Length-dotIndex-1)));
            }
        }

            
        static internal Tuple<string, string> csharpTypeValToSfTypeVal(DbType srcType, object srcVal)
        {
            string destType;
            string destVal;

            switch(srcType)
            {
                case DbType.Decimal:
                case DbType.Int16:
                case DbType.Int32:
                case DbType.Int64:
                case DbType.UInt16:
                case DbType.UInt32:
                case DbType.UInt64:
                case DbType.VarNumeric:
                    destType = SFDataType.FIXED.ToString();
                    destVal = srcVal.ToString();
                    break;

                case DbType.Boolean:
                    destType = SFDataType.BOOLEAN.ToString();
                    destVal = srcVal.ToString();
                    break;

                case DbType.Double:
                    destType = SFDataType.REAL.ToString();
                    destVal = srcVal.ToString();
                    break;

                case DbType.Guid:
                case DbType.String:
                case DbType.StringFixedLength:
                    destType = SFDataType.TEXT.ToString();
                    destVal = srcVal.ToString();
                    break;

                case DbType.Date:
                    destType = SFDataType.DATE.ToString();
                    if (srcVal.GetType() != typeof(DateTime))
                    {
                        throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, 
                            srcVal.GetType().ToString(), DbType.Date.ToString());
                    }
                    else
                    {
                        long millis = (long)((DateTime)srcVal).ToUniversalTime().Subtract(
                            unixEpoch).TotalMilliseconds;
                        destVal = millis.ToString();
                    }
                    break;

                case DbType.Time:
                    destType = SFDataType.TIME.ToString();
                    if (srcVal.GetType() != typeof(DateTime))
                    {
                        throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, 
                            srcVal.GetType().ToString(), DbType.Time.ToString());
                    }
                    else
                    {
                        DateTime srcDt = ((DateTime)srcVal);
                        long nanoSinceMidNight = (long)(srcDt.Ticks - srcDt.Date.Ticks) * 100L; 

                        destVal = nanoSinceMidNight.ToString();
                    }
                    break;

                case DbType.DateTime:
                    destType = SFDataType.TIMESTAMP_NTZ.ToString();
                    if (srcVal.GetType() != typeof(DateTime))
                    {
                        throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, 
                            srcVal.GetType().ToString(), DbType.DateTime.ToString());
                    }
                    else
                    {
                        DateTime srcDt = ((DateTime)srcVal);
                        destVal = ((long)(srcDt.Subtract(unixEpoch).Ticks * 100)).ToString();
                    }
                    break;
                
                // by default map DateTimeoffset to TIMESTAMP_TZ
                case DbType.DateTimeOffset:
                    destType = SFDataType.TIMESTAMP_TZ.ToString();
                    if (srcVal.GetType() != typeof(DateTimeOffset))
                    {
                        throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, 
                            srcVal.GetType().ToString(), DbType.DateTimeOffset.ToString());
                    }
                    else
                    {
                        DateTimeOffset dtOffset = (DateTimeOffset)srcVal;
                        destVal = String.Format("{0} {1}", (dtOffset.UtcTicks - unixEpoch.Ticks) * 100L, 
                            dtOffset.Offset.TotalMinutes + 1440);
                    }
                    break;

                case DbType.Binary:
                    destType = SFDataType.BINARY.ToString();
                    if (srcVal.GetType() != typeof(byte[]))
                    {
                        throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, 
                            srcVal.GetType().ToString(), DbType.Binary.ToString());
                    }
                    else
                    {
                        destVal = bytesToHex((byte[])srcVal);
                    }
                    break;

                default:
                    throw new NotImplementedException();
            }
            return Tuple.Create(destType, destVal);
        }

        static private string bytesToHex(byte[] bytes)
        {
            StringBuilder hexBuilder = new StringBuilder(bytes.Length * 2);
            foreach(byte b in bytes)
            {
                hexBuilder.AppendFormat("{0:x2}", b);
            }
            return hexBuilder.ToString();
        }

        static private byte[] hexToBytes(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        static internal string csharpValToSfVal(SFDataType sfDataType, object srcVal)
        {
            switch(sfDataType)
            {
                case SFDataType.TIMESTAMP_LTZ:
                    if (srcVal.GetType() != typeof(DateTimeOffset))
                    {
                        throw new SnowflakeDbException(SFError.INVALID_DATA_CONVERSION, srcVal, 
                            srcVal.GetType().ToString(), SFDataType.TIMESTAMP_LTZ.ToString());
                    }
                    else
                    {
                       return ((long)(((DateTimeOffset)srcVal).UtcTicks - unixEpoch.Ticks) * 100).ToString();
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        static internal string toDateString(DateTime date, string formatter)
        {
            // change formatter from "YYYY-MM-DD" to "yyyy-MM-dd"
            formatter = formatter.Replace("Y", "y").Replace("m", "M").Replace("D", "d");
            return date.ToString(formatter);
        }
    }
}
