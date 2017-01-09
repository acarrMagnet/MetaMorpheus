﻿using MetaMorpheus;
using System.Collections.Generic;

namespace IndexSearchAndAnalyze
{
    public class IndexParams : MyParams
    {
        public List<MorpheusModification> fixedModifications { get; private set; }
        public List<Protein> proteinList { get; private set; }
        public List<MorpheusModification> localizeableModifications { get; private set; }
        public Protease protease { get; private set; }
        public List<MorpheusModification> variableModifications { get; private set; }

        public IndexParams(List<Protein> proteinList, List<MorpheusModification> variableModifications, List<MorpheusModification> fixedModifications, List<MorpheusModification> localizeableModifications, Protease protease, AllTasksParams a2) : base(a2)
        {
            this.proteinList = proteinList;
            this.variableModifications = variableModifications;
            this.fixedModifications = fixedModifications;
            this.localizeableModifications = localizeableModifications;
            this.protease = protease;
        }

        internal override void Validate()
        {
        }
    }
}