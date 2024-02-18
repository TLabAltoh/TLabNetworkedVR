using System.Collections;

namespace TLab.Network.WebRTC
{
    public enum RTCSigAction
    {
        OFFER,
        ANSWER,
        ICE,
        JOIN,
        EXIT
    }

    [System.Serializable]
    public class RTCICE
    {
        public string sdpMLineIndex;
        public string sdpMid;
        public string candidate;
    }

    [System.Serializable]
    public class RTCDesc
    {
        public int type;
        public string sdp;
    }

    [System.Serializable]
    public class RTCSigJson
    {
        public int action;
        public string room;
        public string src;
        public string dst;
        public RTCDesc desc;
        public RTCICE ice;
    }

    public static class HashTableExtension
    {
        public static bool FirstValue<K, V>(
            this Hashtable hashtable, System.Func<V, bool> callback,
            out K keyResult, out V valueResult) where K : class where V : class
        {
            foreach (var key in hashtable.Keys)
            {
                if (callback.Invoke(hashtable[key] as V))
                {
                    keyResult = key as K;
                    valueResult = hashtable[key] as V;

                    return true;
                }
            }

            keyResult = null;
            valueResult = null;

            return false;
        }
    }

    public static class TLabWebRTCExtensions
    {
        public static string ToJson(this int? value)
        {
            if (value == null)
            {
                return null;
            }

            return value.ToString();
        }

        public static int? TryToInt(this string value)
        {
            if (int.TryParse(value, out int result))
            {
                return result;
            }

            return null;
        }
    }
}
