using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Naratteu.Modbus
{
    public abstract class MobRR<REQ, RES, P_REQ, P_RES> : IPduRR<REQ, RES>
         where REQ : struct, IREQ where P_REQ : struct, IPDU<REQ>
         where RES : struct, IRES where P_RES : struct, IPDU<RES>
    {
        public byte[] reqRaw { get; } = new byte[Marshal.SizeOf<P_REQ>(default)];
        public byte[] resRaw { get; } = new byte[Marshal.SizeOf<P_RES>(default)];
        public ref P_REQ req => ref MemoryMarshal.AsRef<P_REQ>(reqRaw);
        public ref P_RES res => ref MemoryMarshal.AsRef<P_RES>(resRaw);
        public abstract ref REQ reqData { get; }
        public abstract ref RES resData { get; }
        public abstract ref byte unit { get; }
        public abstract ref byte fcode { get; }

        public abstract void ResChk();
        protected void resPduChk()
        {
            if (req.GetUnit() != res.GetUnit())
                throw new Exception("pdu채널 틀림");
            if (req.GetFcode() != res.GetFcode())
                throw new Exception("pdu펑션 틀림");
            IPduRR irr = this;
            irr.ResDataChk();
        }
    }
    public abstract class TcpRR<REQ, RES> : MobRR<REQ, RES, TCP<REQ>, TCP<RES>> where REQ : struct, IREQ where RES : struct, IRES
    {
        public sealed override ref REQ reqData => ref req.pdu.data;
        public sealed override ref RES resData => ref res.pdu.data;
        public ref Endian<UInt16>.Big txid => ref req.txid;
        public ref Endian<UInt16>.Big pid => ref req.pid;
        public sealed override ref byte unit => ref req.pdu.unit;
        public sealed override ref byte fcode => ref req.pdu.fcode;

        public TcpRR(UInt16 txid, UInt16 pid, byte unit, byte fcode, in REQ data)
        {
            req = new()
            {
                txid = txid,
                pid = pid,
                len = (UInt16)Marshal.SizeOf(req.pdu),
                pdu = {
                    unit = unit,
                    fcode = fcode,
                    data = data,
                }
            };
        }

        public sealed override void ResChk()
        {
            if (req.txid != res.txid)
                throw new Exception("tcpTXID 틀림");
            if (res.len != Marshal.SizeOf(res.pdu))
                throw new Exception("tcp길이 틀림");
            resPduChk();
        }
    }
    public abstract class RtuRR<REQ, RES> : MobRR<REQ, RES, RTU<REQ>, RTU<RES>> where REQ : struct, IREQ where RES : struct, IRES
    {
        public sealed override ref REQ reqData => ref req.pdu.data;
        public sealed override ref RES resData => ref res.pdu.data;
        public sealed override ref byte unit => ref req.pdu.unit;
        public sealed override ref byte fcode => ref req.pdu.fcode;

        public RtuRR(byte unit, byte fcode, in REQ data)
        {
            req = new()
            {
                pdu = {
                    unit = unit,
                    fcode = fcode,
                    data = data,
                }
            };
            req.crc = MakeCRC16IBM(reqRaw);
        }

        public sealed override void ResChk()
        {
            if(res.crc != MakeCRC16IBM(resRaw))
                throw new Exception("rtu crc 틀림");
            resPduChk();
        }
        protected static UInt16 MakeCRC16IBM(Span<byte> rtuRaw)
        {
            var pduRaw = rtuRaw[..^2];
            int crc16 = 0xFFFF;
            foreach (byte b in pduRaw)
            {
                crc16 ^= b;
                for (int j = 0; j < 8; j++)
                {
                    var lsb = (crc16 & 1) == 1;
                    crc16 = (crc16 >> 1) & 0x7FFF;
                    if (lsb) crc16 ^= 0xA001;
                }
            }
            return (UInt16)crc16;
        }
    }

    public interface IPduRR
    {
        byte[] reqRaw { get; }
        byte[] resRaw { get; }
        ref byte unit { get; }
        ref byte fcode { get; }
        void ResChk();
        void ResDataChk() { }
    }
    public interface IPduRR<REQ, RES> : IPduRR where REQ : struct, IREQ where RES : struct, IRES
    {
        ref REQ reqData { get; }
        ref RES resData { get; }
    }
    public interface IAttrRR<T> where T : struct
    {
        static readonly MobAttribute attr = MobAttribute.Get<T>();
        static readonly byte fcode = attr.fcode;
        static readonly UInt16 addr = attr.addr;
        static readonly (UInt16 cnt, byte len) Info = attr.Info<T>();
        static readonly UInt16 cnt = Info.cnt;
        static readonly byte len = Info.len;
        static readonly READ ReqRead = attr.ToReqRead<T>();
        static WRITE<T> ReqWrite(in T t) => attr.ToReqWrite(t);
    }
    public interface IReadRR<T> : IPduRR<READ, READ<T>>, IAttrRR<T> where T : struct
    {
        void IPduRR.ResDataChk()
        {
            if (reqData.cnt != resData.len)
                throw new Exception("요청갯수 틀림");
            if (resData.len != attr.Info<T>().len)
                throw new Exception("읽을길이 틀림");
        }
        ref Endian<UInt16>.Big reqAddr => ref reqData.addr;
        ref T resRead => ref resData.read;
    }
    public interface IWriteRR<T> : IPduRR<WRITE<T>, WRITE> where T : struct
    {
        void IPduRR.ResDataChk()
        {
            if (reqData.cnt != resData.cnt)
                throw new Exception("쓴갯수 틀림");
        }
        ref Endian<UInt16>.Big reqAddr => ref reqData.addr;
        ref T reqWrite => ref reqData.write;
    }
    public interface ISetRR : IPduRR<SET, SET>
    {
        void IPduRR.ResDataChk()
        {
            if (reqRaw.SequenceEqual(resRaw) is not true)
                throw new Exception("요청과 응답이 동일하지 않음.");
        }
        ref Endian<UInt16>.Big addr => ref reqData.addr;
        ref Endian<UInt16>.Big reg => ref reqData.reg;
        ref RegSizeBit bit => ref reqData.bit;
    }
    public class TcpRead<T> : TcpRR<READ, READ<T>>, IReadRR<T> where T : struct
    {
        public TcpRead(byte unit, UInt16 txid = 0, UInt16 pid = 0) : base(txid, pid, unit, IAttrRR<T>.fcode, IAttrRR<T>.ReqRead) { }
    }
    public class TcpWrite<T> : TcpRR<WRITE<T>, WRITE>, IWriteRR<T> where T : struct
    {
        public TcpWrite(byte unit, in T write, UInt16 txid = 0, UInt16 pid = 0) : base(txid, pid, unit, IAttrRR<T>.fcode, IAttrRR<T>.ReqWrite(write)) { }
    }
    public class TcpSet : TcpRR<SET, SET>, ISetRR
    {
        public TcpSet(byte unit, byte fcode, UInt16 addr, bool bit, UInt16 txid = 0, UInt16 pid = 0) : base(txid, pid, unit, fcode, new() { addr = addr, bit = bit }) { }
        public TcpSet(byte unit, byte fcode, UInt16 addr, UInt16 reg, UInt16 txid = 0, UInt16 pid = 0) : base(txid, pid, unit, fcode, new() { addr = addr, reg = reg }) { }
    }
    public class RtuRead<T> : RtuRR<READ, READ<T>>, IReadRR<T> where T : struct
    {
        public RtuRead(byte unit) : base(unit, IAttrRR<T>.fcode, IAttrRR<T>.ReqRead) { }
    }
    public class RtuWrite<T> : RtuRR<WRITE<T>, WRITE>, IWriteRR<T> where T : struct
    {
        public RtuWrite(byte unit, in T write) : base(unit, IAttrRR<T>.fcode, IAttrRR<T>.ReqWrite(in write)) { }
    }
    public class RtuSet : RtuRR<SET, SET>, ISetRR
    {
        public RtuSet(byte unit, byte fcode, UInt16 addr, bool bit) : base(unit, fcode, new() { addr = addr, bit = bit }) { }
        public RtuSet(byte unit, byte fcode, UInt16 addr, UInt16 reg) : base(unit, fcode, new() { addr = addr, reg = reg }) { }
    }
}