using KSP.UI.Screens;
using System.Collections.Generic;
using UnityEngine;

namespace ForScience {
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class ForScience : MonoBehaviour {
        //GUI
        ApplicationLauncherButton FSAppButton;

        //states
        Vessel stateVessel;
        CelestialBody stateBody;
        string stateBiome;
        ExperimentSituations stateSituation = 0;

        //thread control
        bool autoTransfer = true;

        // to do list
        //
        // integrate science lab
        // allow a user specified container to hold data
        // transmit data from probes automaticly

        void Awake() {
            GameEvents.onGUIApplicationLauncherReady.Add(setupAppButton);
        }

        void OnDestroy() {
            GameEvents.onGUIApplicationLauncherReady.Remove(setupAppButton);
            if (FSAppButton != null)
                ApplicationLauncher.Instance.RemoveModApplication(FSAppButton);
        }

        void setupAppButton() {
            if (FSAppButton == null) {
                FSAppButton = ApplicationLauncher.Instance.AddModApplication(
                        onTrue: toggleCollection,
                        onFalse: toggleCollection,
                        onHover: null,
                        onHoverOut: null,
                        onEnable: null,
                        onDisable: null,
                        visibleInScenes: ApplicationLauncher.AppScenes.FLIGHT,
                        texture: getIconTexture(autoTransfer)
                );
            }
        }

        // running in physics update so that the vessel is always in a valid state to check for science.
        void FixedUpdate() {
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX) {
                if (FSAppButton == null)
                    setupAppButton();
                if (autoTransfer) {
                    if (StatesHaveChanged()) {
                        RunScience();
                    }
                    if (HasContainer) {
                        TransferScience();
                    }
                }
            }
        }

        void TransferScience() // automaticlly find, transer and consolidate science data on the vessel
        {
            if (ActiveContainer().GetActiveVesselDataCount() != ActiveContainer().GetScienceCount()) // only actually transfer if there is data to move
            {

                Debug.Log("[ForScience!] Transfering science to container.");

                ActiveContainer().StoreData(GetExperimentListAsInterface(), true); // this is what actually moves the data to the active container
                var containerstotransfer = ContainerListAsInterface(); // a temporary list of our containers
                containerstotransfer.Remove(ActiveContainer()); // we need to remove the container we storing the data in because that would be wierd and buggy
                ActiveContainer().StoreData(containerstotransfer, true); // now we store all data from other containers
            }
        }

        void RunScience() {
            if (GetExperimentList() == null) {
                Debug.Log("[ForScience!] There are no experiments.");
                return;
            }

            var experimentlist = GetExperimentList();
            for (int i = 0; i < experimentlist.Count; i++) {
                var currentExperiment = experimentlist[i];
                Debug.Log("[ForScience!] Checking experiment: " + currentScienceSubject(currentExperiment.experiment).id);

                if (currentExperiment.GetData().Length > 0) {
                    Debug.Log("[ForScience!] Skipping: Experiemnt already has data.");
                } else if (ActiveContainer() && ActiveContainer().HasData(newScienceData(currentExperiment))) {
                    Debug.Log("[ForScience!] Skipping: We already have that data onboard.");
                } else if (!currentExperiment.experiment.IsUnlocked()) {
                    Debug.Log("[ForScience!] Skipping: Experiment is not unlocked.");
                } else if (!currentExperiment.rerunnable && !IsScientistOnBoard()) {
                    Debug.Log("[ForScience!] Skipping: Experiment is not repeatable.");
                } else if (!currentExperiment.experiment.IsAvailableWhile(currentSituation(), body)) {
                    Debug.Log("[ForScience!] Skipping: Experiment is not available for this situation/atmosphere.");
                } else if (currentScienceValue(currentExperiment.experiment) < 0.1) {
                    Debug.Log("[ForScience!] Skipping: No more science is available: ");
                } else {
                    Debug.Log("[ForScience!] Running experiment: " + currentScienceSubject(currentExperiment.experiment).id);
                    DeployExperiment(currentExperiment);
                }
            }
        }

        /* run experiment without popping up the report */
        private void DeployExperiment(ModuleScienceExperiment currentExperiment) {
            var temp = currentExperiment.useStaging;
            currentExperiment.useStaging = true;
            currentExperiment.OnActive();
            currentExperiment.useStaging = temp;
        }

        private bool surfaceSamplesUnlocked() // checking that the appropriate career unlocks are flagged
        {
            return GameVariables.Instance.UnlockedEVA(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex))
                && GameVariables.Instance.UnlockedFuelTransfer(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.ResearchAndDevelopment));
        }

        float currentScienceValue(ScienceExperiment experiment) {
            return ResearchAndDevelopment.GetScienceValue(experiment.baseValue * experiment.dataScale, currentScienceSubject(experiment)) * HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
        }

        ScienceData newScienceData(ModuleScienceExperiment currentExperiment) // construct our own science data for an experiment
        {
            return new ScienceData(
                       amount: currentExperiment.experiment.baseValue * currentScienceSubject(currentExperiment.experiment).dataScale,
                       xmitValue: currentExperiment.xmitDataScalar,
                       xmitBonus: 0f,
                       id: currentScienceSubject(currentExperiment.experiment).id,
                       dataName: currentScienceSubject(currentExperiment.experiment).title
                       );
        }

        private Vessel vessel { get { return FlightGlobals.ActiveVessel; } }

        private CelestialBody body { get { return vessel.mainBody; } }

        ExperimentSituations currentSituation() {
            return ScienceUtil.GetExperimentSituation(vessel);
        }

        private string BiomeString(ScienceExperiment experiment = null) {
            if (experiment != null && !experiment.BiomeIsRelevantWhile(ScienceUtil.GetExperimentSituation(vessel)))
                return string.Empty;
            if (vessel == null || body == null)
                return string.Empty;
            if (body.BiomeMap == null)
                return string.Empty;
            var v = vessel.EVALadderVessel;
            if (!string.IsNullOrEmpty(v.landedAt))
                return Vessel.GetLandedAtString(v.landedAt);
            else
                return ScienceUtil.GetExperimentBiome(body, v.latitude, v.longitude);
        }

        ScienceSubject currentScienceSubject(ScienceExperiment experiment) {
            return ResearchAndDevelopment.GetExperimentSubject(experiment, ScienceUtil.GetExperimentSituation(vessel), body, BiomeString(experiment));
        }

        // set the container to gather all science data inside, usualy this is the root command pod of the oldest vessel
        ModuleScienceContainer ActiveContainer() {
            try {
                return ContainerList()[0];
            }
            catch (System.ArgumentOutOfRangeException) {
                return null;
            }
        }

        private bool HasContainer { get { return ContainerList().Count > 1; } }

        // all ModuleScienceExperiments
        List<ModuleScienceExperiment> GetExperimentList() {
            return vessel.FindPartModulesImplementing<ModuleScienceExperiment>();
        }

        // all ModuleScienceExperiments as IScienceDataContainers
        List<IScienceDataContainer> GetExperimentListAsInterface() {
            List<IScienceDataContainer> iexperiments = new List<IScienceDataContainer>();
            var experiments = GetExperimentList();
            for (int i = 0; i < experiments.Count; i++) {
                iexperiments.Add((IScienceDataContainer)experiments[i]);
            }
            return iexperiments;
        }

        // all ModuleScienceContainers
        List<ModuleScienceContainer> ContainerList()
        {
            return vessel.FindPartModulesImplementing<ModuleScienceContainer>();
        }

        // all ModuleScienceContainers as IScienceDataContainers
        List<IScienceDataContainer> ContainerListAsInterface() {
            List<IScienceDataContainer> icontainers = new List<IScienceDataContainer>();
            var containers = ContainerList();
            for (int i = 0; i < containers.Count; i++) {
                icontainers.Add((IScienceDataContainer)containers[i]);
            }
            return icontainers;
        }

        bool StatesHaveChanged() {
            if (vessel != stateVessel | currentSituation() != stateSituation | body != stateBody | BiomeString() != stateBiome) {
                stateVessel = vessel;
                stateBody = body;
                stateSituation = currentSituation();
                stateBiome = BiomeString();
                return true;
            }
            else return false;
        }

        void toggleCollection() // This is our main toggle for the logic and changes the icon between green and red versions on the bar when it does so.
        {
            autoTransfer = !autoTransfer;
            FSAppButton.SetTexture(getIconTexture(autoTransfer));
        }

        // check if there is a scientist onboard so we can rerun things like goo or scijrs
        bool IsScientistOnBoard() {
            var crewlist = vessel.GetVesselCrew();
            for (int i = 0; i < crewlist.Count; i++) {
                if ( crewlist[i].experienceTrait.Title == "Scienctist" )
                    return true;
            }
            return false;
        }

        Texture2D getIconTexture(bool b) {
            if (b)
                return GameDatabase.Instance.GetTexture("ForScienceRedux/Icons/FS_active", false);
            else
                return GameDatabase.Instance.GetTexture("ForScienceRedux/Icons/FS_inactive", false);
        }
    }
}
