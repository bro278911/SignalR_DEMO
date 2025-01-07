using System;

namespace signalR_test.Models
{
    public class ChargingData
    {
        public string ChargingID { get; set; }
        public string SiteID { get; set; }
        public DateTime ReceivedTime { get; set; }
        public DateTime Datatime { get; set; }
        public int Sequence { get; set; }
        public string PowerStatus { get; set; }
        public string PcsStatus { get; set; }
        public string AcbStatus { get; set; }
        public decimal Soc { get; set; }
        public decimal KWh { get; set; }
        public decimal PcsPower { get; set; }
        public decimal SitePower { get; set; }
    }
}
