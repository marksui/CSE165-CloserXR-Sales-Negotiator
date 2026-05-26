using System;

namespace CloserXR.SalesNegotiator
{
    public enum SalesIntent
    {
        Neutral,
        PricePushback,
        Rejection,
        Agreement,
        Uncertain,
        Closing
    }

    public static class SalesIntentClassifier
    {
        private static readonly string[] PricePushbackKeywords =
        {
            "too expensive",
            "price",
            "cost",
            "overpriced",
            "can't afford",
            "cannot afford",
            "budget"
        };

        private static readonly string[] RejectionKeywords =
        {
            "no",
            "not interested",
            "don't want",
            "do not want",
            "pass",
            "stop"
        };

        private static readonly string[] AgreementKeywords =
        {
            "yes",
            "deal",
            "i'm in",
            "im in",
            "sounds good",
            "sign me up"
        };

        private static readonly string[] UncertaintyKeywords =
        {
            "maybe",
            "not sure",
            "think about it",
            "let me think",
            "hmm"
        };

        private static readonly string[] ClosingKeywords =
        {
            "limited time",
            "right now",
            "close",
            "deal",
            "guarantee",
            "opportunity",
            "investment"
        };

        public static SalesIntent ClassifyUserText(string text)
        {
            if (ContainsAny(text, AgreementKeywords))
            {
                return SalesIntent.Agreement;
            }

            if (ContainsAny(text, PricePushbackKeywords))
            {
                return SalesIntent.PricePushback;
            }

            if (ContainsAny(text, RejectionKeywords))
            {
                return SalesIntent.Rejection;
            }

            if (ContainsAny(text, UncertaintyKeywords))
            {
                return SalesIntent.Uncertain;
            }

            return SalesIntent.Neutral;
        }

        public static SalesIntent ClassifyAgentText(string text)
        {
            return ContainsAny(text, ClosingKeywords) ? SalesIntent.Closing : SalesIntent.Neutral;
        }

        public static bool ContainsAny(string text, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (string keyword in keywords)
            {
                if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
