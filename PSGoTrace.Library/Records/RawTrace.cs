using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PSGoTrace.Library.Records;

namespace TraceViewer.Trace.Records
{
    internal struct RawEvent
    {
        public RawEvent(EventType type, int offset)
        {
            Offset = offset;
            Type = type;
            Args = null!;
            StringArgs = null;
        }

        internal int Offset { get; }
        internal EventType Type { get; }
        internal ulong[] Args { get; set; }
        internal string[]? StringArgs { get; set; }
    }

    internal readonly struct RawTrace
    {
        public IReadOnlyList<RawEvent> Events { get; }
        public IReadOnlyDictionary<ulong, string> Strings { get; }
        public int Version { get; }

        private RawTrace(IReadOnlyList<RawEvent> events, IReadOnlyDictionary<ulong, string> strings, int version)
        {
            Events = events;
            Strings = strings;
            Version = version;
        }

        private static readonly int[] SupportedVersions = {1005, 1007, 1008, 1009, 1010, 1011};

        public static RawTrace Load(Stream source, bool leaveOpen)
        {
            var rawEvents = new List<RawEvent>();
            var rawStrings = new Dictionary<ulong, string>();
            using BinaryReader reader = new BinaryReader(source, Encoding.UTF8, leaveOpen);
            var version = ParseHeader(reader);
            if (!SupportedVersions.Contains(version))
                throw new InvalidTraceException(
                    $"unsupported trace file version {version / 1000}.{version % 1000}");

            var end = reader.BaseStream.Length;
            while (reader.BaseStream.Position < end)
            {
                var eventStartPosition = reader.BaseStream.Position;
                var eventTypeAndArgsCount = reader.ReadByte();
                var type = (EventType) (eventTypeAndArgsCount & ~(0b11 << 6));
                var argsCount = (eventTypeAndArgsCount >> 6) + 1;
                var inlineArgs = (byte) 4;
                if (version < 1007)
                {
                    argsCount++;
                    inlineArgs++;
                }

                if (type == EventType.None || EventDescription.Of(type).MinVersion > version)
                    throw new InvalidTraceException("unknown type");

                if (type == EventType.String)
                {
                    // String dictionary entry [ID, length, string].
                    var id = ReadVal(reader);
                    if (id == 0)
                        throw new InvalidTraceException($"{source.Position} has invalid id 0");
                    if (rawStrings.ContainsKey(id))
                        throw new InvalidTraceException($"{source.Position} has duplicate id {id}");
                    var value = ReadStr(reader);
                    if (value.Length == 0)
                        throw new InvalidTraceException($"{source.Position} has invalid length 0");
                    rawStrings[id] = value;
                    continue;
                }

                var ev = new RawEvent(type, (int) eventStartPosition);
                if (argsCount < inlineArgs)
                {
                    ev.Args = new ulong[argsCount];
                    for (var i = 0; i < argsCount; i++)
                        ev.Args[i] = ReadVal(reader);
                }
                else
                {
                    var evLength = ReadVal(reader);
                    var start = source.Position;
                    var buffer = new List<ulong>();
                    while (evLength > (ulong) (source.Position - start))
                    {
                        var arg = ReadVal(reader);
                        buffer.Add(arg);
                    }

                    if (evLength != (ulong) (source.Position - start))
                        throw new InvalidTraceException(
                            $"event has wrong length at {source.Position}, want: {evLength}, got: {source.Position - start}");
                    ev.Args = buffer.ToArray();
                }

                if (ev.Type == EventType.UserLog) ev.StringArgs = new[] {ReadStr(reader)};

                rawEvents.Add(ev);
            }

            return new RawTrace(rawEvents, rawStrings, version);
        }

        private static string ReadStr(BinaryReader reader)
        {
            var size = ReadVal(reader);
            if (size == 0)
                return "";
            if (size > 1e6)
                throw new InvalidTraceException($"string at {reader.BaseStream.Position} has incorrect size");
            return Encoding.UTF8.GetString(reader.ReadBytes((int) size));
        }

        private static ulong ReadVal(BinaryReader reader)
        {
            var value = 0ul;
            for (var i = 0; i < 10; i++)
            {
                var data = reader.ReadByte();
                value |= (ulong) (data & 0x7f) << (i * 7);
                if ((data & 0x80) == 0) return value;
            }

            throw new InvalidTraceException($"bad value at offset {reader.BaseStream.Position}");
        }

        private static readonly byte[] FileHeader = Encoding.UTF8.GetBytes(" trace\x00\x00\x00\x00");

        private static int ParseHeader(BinaryReader reader)
        {
            var buf = reader.ReadBytes(16);
            if (buf[0] != 'g' || buf[1] != 'o' || buf[2] != ' ' ||
                buf[3] < '1' || buf[3] > '9' ||
                buf[4] != '.' ||
                buf[5] < '1' || buf[5] > '9')
                throw new InvalidTraceException("bad trace header");

            var version = buf[5] - '0';
            var i = 0;
            for (; buf[6 + i] >= '0' && buf[6 + i] <= '9' && i < 2; i++)
                version = version * 10 + (buf[6 + i] - '0');
            version += (buf[3] - '0') * 1000;

            if (!buf[(6 + i)..].SequenceEqual(FileHeader[..(10 - i)]))
                throw new InvalidTraceException("bad trace header");
            return version;
        }
    }
}