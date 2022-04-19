using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Naratteu.Modbus
{
    using Reg = Endian<UInt16>.Big;

    public unsafe struct Swap<T> where T : unmanaged
    {
        T raw;
        readonly static int size = sizeof(T);
        delegate void SwapAction(byte[] swap, Span<byte> raw, Span<byte> val, int i);
        readonly static SwapAction get = static (swap, raw, val, i) => { val[swap[i]] = raw[i]; };
        readonly static SwapAction set = static (swap, raw, val, i) => { raw[i] = val[swap[i]]; };
        T prop(SwapAction met, byte[] swap, T value = default)
        {
            fixed (void* _this_ = &raw)
            {
                var raw = new Span<byte>(_this_, size);
                var val = new Span<byte>(&value, size);
                for (int i = 0; i < size; i++)
                    met(swap, raw, val, i);
            }
            return value;
        }
        public T this[params byte[] swap]
        {
            get => prop(get, swap);
            set => prop(set, swap, value);
        }
    }
    
    public unsafe static class Endian<T> where T : unmanaged
    {
        readonly static int size = sizeof(T);
        static T rev(T t) { new Span<byte>(&t, size).Reverse(); return t; }

        public struct Big
        {
            T big; //메모리에는 BigEndian 기준으로 담겨있음.
            readonly static Func<T, T> ifRev = BitConverter.IsLittleEndian ? rev : t => t;
            public static implicit operator T(Big b) => ifRev(b.big);
            public static implicit operator Big(T t) => new() { big = ifRev(t) };

            public override string ToString()
            {
                var bytes = new byte[size];
                MemoryMarshal.AsRef<T>(bytes) = big;
                var hex = BitConverter.ToString(bytes);
                return $"{(T)this}({hex}){nameof(big)}";
            }
        }
        public struct Lit
        {
            T lit; //메모리에는 LittleEndian 기준으로 담겨있음.
            readonly static Func<T, T> ifRev = BitConverter.IsLittleEndian ? t => t : rev;
            public static implicit operator T(Lit b) => ifRev(b.lit);
            public static implicit operator Lit(T t) => new() { lit = ifRev(t) };

            public override string ToString()
            {
                var bytes = new byte[size];
                MemoryMarshal.AsRef<T>(bytes) = lit;
                var hex = BitConverter.ToString(bytes);
                return $"{(T)this}({hex}){nameof(lit)}";
            }
        }
    }

    public interface IPDU<D> where D : struct, IDATA { byte GetUnit(); byte GetFcode(); }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TCP<D> : IPDU<D> where D : struct, IDATA { public Reg txid, pid, len; public PDU<D> pdu; public byte GetUnit() => pdu.unit; public byte GetFcode() => pdu.fcode; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RTU<D> : IPDU<D> where D : struct, IDATA { public PDU<D> pdu; public Endian<UInt16>.Lit crc; public byte GetUnit() => pdu.unit; public byte GetFcode() => pdu.fcode; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] //type:일케하면 모든 struct에 적용되는게 맞는지?
    public struct PDU<D> where D : struct, IDATA { public byte unit, fcode; public D data; }

    public interface IDATA { }
    public interface IREQ : IDATA { }
    public interface IRES : IDATA { }
    public interface IDATA<T> : IDATA { T data(); }
    public struct READ : IREQ
    {
        public Reg addr, cnt;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] //type:일케하면 모든 struct에 적용되는게 맞는지?
    public struct READ<T> : IRES, IDATA<T> where T : struct
    {
        public byte len; public T read;
        public T data() => read;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] //type:일케하면 모든 struct에 적용되는게 맞는지?
    public struct WRITE<T> : IREQ, IDATA<T> where T : struct
    {
        public Reg addr, cnt; public byte len; public T write;
        public T data() => write;
    }
    public struct WRITE : IRES
    {
        public Reg addr, cnt;
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct SET : IREQ, IRES
    {
        [FieldOffset(0)] public Reg addr;
        [FieldOffset(2)] public Reg reg;
        [FieldOffset(2)] public RegSizeBit bit;
    }
    public struct RegSizeBit
    {
        byte b_, _b;
        public static implicit operator bool(RegSizeBit r) => r switch
        {
            { b_: 0xFF, _b: 0x00 } => true,
            { b_: 0x00, _b: 0x00 } => false,
            _ => throw new NotImplementedException($"Coil이 아닙니다. {new { r.b_, r._b }}"),
        };
        public static implicit operator RegSizeBit(bool b) => b switch
        {
            true => new() { b_ = 0xFF, _b = 0x00 },
            false => new() { b_ = 0x00, _b = 0x00 },
        };
    }
}
