using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Naratteu.Modbus
{
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
    public abstract class MobAttribute : Attribute
    {
        public byte fcode { get; init; }
        public UInt16 addr { get; init; }

        public abstract (UInt16 cnt, byte len) Info<T>() where T : struct;

        public static MobAttribute Get<T>() where T : struct => (MobAttribute)typeof(T).GetCustomAttributes(typeof(MobAttribute), true).Single();
        public READ ToReqRead<T>() where T : struct => new() { addr = addr, cnt = Info<T>().cnt };
        public WRITE<T> ToReqWrite<T>(in T t) where T : struct
        {
            (UInt16 cnt, byte len) = Info<T>();
            return new() { addr = addr, cnt = cnt, len = len, write = t };
        }
    }

    public class MobRegAttribute : MobAttribute
    {
        public override (UInt16 cnt, byte len) Info<T>() where T : struct
        {
            var len = Marshal.SizeOf<T>(default);
            var cnt = len / 2;
            return ((UInt16)cnt, (byte)len);
        }
    }

    public class MobBitAttribute : MobAttribute
    {
        public UInt16 cnt { get; init; }
        public override (UInt16 cnt, byte len) Info<T>() where T : struct
        {
            var len = (cnt + 7) / 8;
            if (len != Marshal.SizeOf<T>(default))
                throw new Exception("요구하는 bit count가 byte size범위를 벗어났습니다.");
            return ((UInt16)cnt, (byte)len);
        }
    }
}
