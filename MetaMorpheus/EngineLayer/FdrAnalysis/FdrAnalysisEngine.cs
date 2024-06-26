﻿using System;
using System.Collections.Generic;
using System.Linq;
using EngineLayer;
using EngineLayer.FdrAnalysis;

namespace EngineLayer.FdrAnalysis
{
    public class FdrAnalysisEngine : MetaMorpheusEngine
    {
        private List<SpectralMatch> AllPsms;
        private readonly int MassDiffAcceptorNumNotches;
        private readonly double ScoreCutoff;
        private readonly string AnalysisType;
        private readonly string OutputFolder; // used for storing PEP training models  
        private readonly bool DoPEP;

        public FdrAnalysisEngine(List<SpectralMatch> psms, int massDiffAcceptorNumNotches, CommonParameters commonParameters,
            List<(string fileName, CommonParameters fileSpecificParameters)> fileSpecificParameters, List<string> nestedIds, string analysisType = "PSM", bool doPEP = true, string outputFolder = null) : base(commonParameters, fileSpecificParameters, nestedIds)
        {
            AllPsms = psms.OrderByDescending(p => p).ToList();
            MassDiffAcceptorNumNotches = massDiffAcceptorNumNotches;
            ScoreCutoff = commonParameters.ScoreCutoff;
            AnalysisType = analysisType;
            this.OutputFolder = outputFolder;
            this.DoPEP = doPEP;
            if (fileSpecificParameters == null) throw new ArgumentNullException("file specific parameters cannot be null");
        }

        protected override MetaMorpheusEngineResults RunSpecific()
        {
            FdrAnalysisResults myAnalysisResults = new FdrAnalysisResults(this, AnalysisType);

            Status("Running FDR analysis...");
            DoFalseDiscoveryRateAnalysis(myAnalysisResults);
            Status("Done.");
            myAnalysisResults.PsmsWithin1PercentFdr = AllPsms.Count(b => b.FdrInfo.QValue <= 0.01 && !b.IsDecoy);

            return myAnalysisResults;
        }

        private void DoFalseDiscoveryRateAnalysis(FdrAnalysisResults myAnalysisResults)
        {
            // Stop if canceled
            if (GlobalVariables.StopLoops) { return; }

            // calculate FDR on a per-protease basis (targets and decoys for a specific protease)
            var psmsGroupedByProtease = AllPsms.GroupBy(p => p.DigestionParams.Protease);

            foreach (var proteasePsms in psmsGroupedByProtease)
            {
                var psms = proteasePsms.ToList();

                QValueTraditional(psms);
                if (psms.Count > 100)
                {
                    if (DoPEP)
                    {
                        Compute_PEPValue(myAnalysisResults);
                    }
                    QValueInverted(psms);
                }
                CountPsm(psms);
            }
        }

        private static void QValueInverted(List<SpectralMatch> psms)
        {
            psms.Reverse();
            //this calculation is performed from bottom up. So, we begin the loop by computing qValue
            //and qValueNotch for the last/lowest scoring psm in the bunch
            double qValue = (psms[0].FdrInfo.CumulativeDecoy + 1) / psms[0].FdrInfo.CumulativeTarget;
            double qValueNotch = (psms[0].FdrInfo.CumulativeDecoyNotch + 1) / psms[0].FdrInfo.CumulativeTargetNotch;

            //Assign FDR values to PSMs
            for (int i = 0; i < psms.Count; i++)
            {
                // Stop if canceled
                if (GlobalVariables.StopLoops) { break; }

                qValue = Math.Min(qValue, (psms[i].FdrInfo.CumulativeDecoy + 1) / psms[i].FdrInfo.CumulativeTarget);
                qValueNotch = Math.Min(qValueNotch, (psms[i].FdrInfo.CumulativeDecoyNotch + 1) / psms[i].FdrInfo.CumulativeTargetNotch);

                double pep = psms[i].FdrInfo == null ? double.NaN : psms[i].FdrInfo.PEP;
                double pepQValue = psms[i].FdrInfo == null ? double.NaN : psms[i].FdrInfo.PEP_QValue;

                psms[i].SetQandPEPvalues(qValue, qValueNotch, pep, pepQValue);

            }
            psms.Reverse(); //we inverted the psms for this calculation. now we need to put them back into the original order
        }

        private void QValueTraditional(List<SpectralMatch> psms)
        {
            double cumulativeTarget = 0;
            double cumulativeDecoy = 0;

            //set up arrays for local FDRs
            double[] cumulativeTargetPerNotch = new double[MassDiffAcceptorNumNotches + 1];
            double[] cumulativeDecoyPerNotch = new double[MassDiffAcceptorNumNotches + 1];

            //Assign FDR values to PSMs
            for (int i = 0; i < psms.Count; i++)
            {
                // Stop if canceled
                if (GlobalVariables.StopLoops) { break; }

                SpectralMatch psm = psms[i];
                int notch = psm.Notch ?? MassDiffAcceptorNumNotches;
                if (psm.IsDecoy)
                {
                    // the PSM can be ambiguous between a target and a decoy sequence
                    // in that case, count it as the fraction of decoy hits
                    // e.g. if the PSM matched to 1 target and 2 decoys, it counts as 2/3 decoy
                    double decoyHits = 0;
                    double totalHits = 0;
                    var hits = psm.BestMatchingBioPolymersWithSetMods.GroupBy(p => p.Peptide.FullSequence);
                    foreach (var hit in hits)
                    {
                        if (hit.First().Peptide.Parent.IsDecoy)
                        {
                            decoyHits++;
                        }
                        totalHits++;
                    }

                    cumulativeDecoy += decoyHits / totalHits;
                    cumulativeDecoyPerNotch[notch] += decoyHits / totalHits;
                }
                else
                {
                    cumulativeTarget++;
                    cumulativeTargetPerNotch[notch]++;
                }

                double qValue = Math.Min(1, cumulativeDecoy / cumulativeTarget);
                double qValueNotch = Math.Min(1, cumulativeDecoyPerNotch[notch] / cumulativeTargetPerNotch[notch]);

                double pep = psm.FdrInfo == null ? double.NaN : psm.FdrInfo.PEP;
                double pepQValue = psm.FdrInfo == null ? double.NaN : psm.FdrInfo.PEP_QValue;

                psm.SetFdrValues(cumulativeTarget, cumulativeDecoy, qValue, cumulativeTargetPerNotch[notch], cumulativeDecoyPerNotch[notch], qValueNotch, pep, pepQValue);
            }
        }

        public void Compute_PEPValue(FdrAnalysisResults myAnalysisResults)
        {
            if (AnalysisType == "PSM")
            {
                //Need some reasonable number of PSMs to train on to get a reasonable estimation of the PEP
                if (AllPsms.Count > 100)
                {
                    string searchType = "standard";
                    if (AllPsms[0].DigestionParams.Protease.Name == "top-down")
                    {
                        searchType = "top-down";
                    }

                    myAnalysisResults.BinarySearchTreeMetrics = PEP_Analysis_Cross_Validation.ComputePEPValuesForAllPSMsGeneric(AllPsms, searchType, this.FileSpecificParameters, this.OutputFolder);

                    Compute_PEPValue_Based_QValue(AllPsms);
                }
            }

            if (AnalysisType == "Peptide")
            {
                Compute_PEPValue_Based_QValue(AllPsms);
            }

            if (AnalysisType == "crosslink" && AllPsms.Count > 100)
            {
                myAnalysisResults.BinarySearchTreeMetrics = PEP_Analysis_Cross_Validation.ComputePEPValuesForAllPSMsGeneric(AllPsms, "crosslink", this.FileSpecificParameters, this.OutputFolder);
                Compute_PEPValue_Based_QValue(AllPsms);
            }
        }

        public static void Compute_PEPValue_Based_QValue(List<SpectralMatch> psms)
        {
            double[] allPEPValues = psms.Select(p => p.FdrInfo.PEP).ToArray();
            int[] psmsArrayIndicies = Enumerable.Range(0, psms.Count).ToArray();
            Array.Sort(allPEPValues, psmsArrayIndicies);//sort the second thing by the first

            double runningSum = 0;
            for (int i = 0; i < allPEPValues.Length; i++)
            {
                runningSum += allPEPValues[i];
                double qValue = runningSum / (i + 1);
                psms[psmsArrayIndicies[i]].FdrInfo.PEP_QValue = Math.Round(qValue, 6);
            }
        }
        /// <summary>
        /// This method gets the count of PSMs with the same full sequence (with q-value < 0.01) to include in the psmtsv output
        /// </summary>
        public void CountPsm(List<SpectralMatch> proteasePsms)
        {
            // exclude ambiguous psms and has a fdr cutoff = 0.01
            var allUnambiguousPsms = proteasePsms.Where(psm => psm.FullSequence != null).ToList();

            var unambiguousPsmsLessThanOnePercentFdr = allUnambiguousPsms.Where(psm =>
                    psm.FdrInfo.QValue <= 0.01
                    && psm.FdrInfo.QValueNotch <= 0.01)
                .GroupBy(p => p.FullSequence);

            Dictionary<string, int> sequenceToPsmCount = new Dictionary<string, int>();

            foreach (var sequenceGroup in unambiguousPsmsLessThanOnePercentFdr)
            {
                sequenceToPsmCount.TryAdd(sequenceGroup.First().FullSequence, sequenceGroup.Count());
            }

            foreach (SpectralMatch psm in allUnambiguousPsms)
            {
                if (sequenceToPsmCount.TryGetValue(psm.FullSequence, out int count))
                {
                    psm.PsmCount = count;
                }
            }
        }
    }
}