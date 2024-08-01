namespace TradeKit.Core.ML
{
    public class Prediction
    {
        // Original label.
        public float Index { get; set; }
        // Predicted score from the trainer.
        public float Score { get; set; }
    }
}
