namespace TradeKit.Gartley
{
    internal class GartleyPattern
    {
        public GartleyPattern(GartleyPatternType patternType,
            double[] xbValues,
            double[] xdValues,
            double[] bdValues,
            double[] acValues,
            GartleySetupType setupType = GartleySetupType.AD)
        {
            PatternType = patternType;
            XbValues = xbValues;
            XdValues = xdValues;
            BdValues = bdValues;
            AcValues = acValues;
            SetupType = setupType;
        }

        public GartleyPatternType PatternType { get; set; }
        public double[] XbValues { get; set; }
        public double[] XdValues { get; set; }
        public double[] BdValues { get; set; }
        public double[] AcValues { get; set; }
        public GartleySetupType SetupType { get; set; }

        public void Deconstruct(out GartleyPatternType patternType, out double[] xbValues, out double[] xdValues, out double[] bdValues, out double[] acValues, out GartleySetupType setupType)
        {
            patternType = PatternType;
            xbValues = XbValues;
            xdValues = XdValues;
            bdValues = BdValues;
            acValues = AcValues;
            setupType = SetupType;
        }
    }
}
