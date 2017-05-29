using KSP.UI.Screens;
using System.Collections.Generic;
using UnityEngine;

namespace MoarScience {
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class MoarScience : MonoBehaviour {
        //GUI
        ApplicationLauncherButton FSAppButton;

        //states
        Vessel stateVessel;
        CelestialBody stateBody;
        string stateBiome;
        ExperimentSituations stateSituation = 0;

        //thread control
        bool autoTransfer = true;

        HashSet<string> TransmittingScience = new HashSet<string>();

        // to do list
        //
        // integrate science lab
        // allow a user specified container to hold data
        // transmit data from probes automaticly

        void Awake() {
            GameEvents.onGUIApplicationLauncherReady.Add(setupAppButton);
            GameEvents.OnScienceRecieved.Add(OnScienceReceived);
            TransmittingScience.Clear();
        }

        void OnDestroy() {
            GameEvents.onGUIApplicationLauncherReady.Remove(setupAppButton);
            if (FSAppButton != null)
                ApplicationLauncher.Instance.RemoveModApplication(FSAppButton);
            GameEvents.OnScienceRecieved.Remove(OnScienceReceived);
        }

        void OnScienceReceived(float scienceAmount, ScienceSubject subject, ProtoVessel vessel, bool recoveryData) {
            Debug.Log("[MoarScience!] removing from transmitting science tracker: " + subject.title);
            TransmittingScience.Remove(subject.title);
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
        // FIXME: this seems like a hack?
        void FixedUpdate() {
            // FIXME: should use vessel.IsControllable to (optionally?) lock out when vessel is not controllable
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX) {
                if (FSAppButton == null)
                    setupAppButton();
                if (autoTransfer) {
                    // FIXME? we may want to runscience+transferscience in a loop until nothing changes?
                    if (StatesHaveChanged()) {
                        RunScience();
                    }
                    if (HasContainer) {
                        TransferScience();
                    } else {
                        Debug.Log("[Moar Science!] No container to move science to.");
                    }
                    TransmitScience();
                }
            }
        }

        // transmit science
        void TransmitScience() {
            if (transmitter == null) {
                Debug.Log("[MoarScience!] No transmitter, not transmitting any science.");
                return;
            }

            var sciencelist = ScienceModsAsInterface();
            for (int i = 0; i < sciencelist.Count; i++) {
                var container = sciencelist[i];
                ScienceData[] datalist = container.GetData();
                for (int j = 0; j < datalist.Length; j++) {
                    var data = datalist[j];
                    TransmitData(data, container);
                }
            }
        }

        private IScienceDataTransmitter transmitter { get { return ScienceUtil.GetBestTransmitter(vessel); } }

        void TransmitData(ScienceData data, IScienceDataContainer container) {
            if ( TransmittingScience.Contains(data.title) ) {
                Debug.Log("[MoarScience!] transmitting queue already has: " + data.title);
                return;
            }
            if ( data.baseTransmitValue < 0.40 ) {
                Debug.Log("[MoarScience!] transmit value is less than 40%: " + data.title);
                return;
            }
            TransmittingScience.Add(data.title);
            transmitter.TransmitData(new List<ScienceData> { data });
            container.DumpData(data);
        }

        // FIXME: this should probably fire only on changes in vessel state and science callbacks
        void TransferScience() {
            if (ActiveContainer().GetActiveVesselDataCount() == ActiveContainer().GetScienceCount()) {
                // shortcut: the activecontainer already has all the science on the vessel
                Debug.Log("[MoarScience!] Target container already has all vessel science.");
                return;
            }

            // if we have dup experiments we will grind here
            Debug.Log("[MoarScience!] Iterating through containers.");

            var scienceList = ScienceModsAsInterface();
            scienceList.Remove(ActiveContainer());
            ActiveContainer().StoreData(scienceList, false);
        }

        // collect science
        void RunScience() {
            if (GetExperimentList() == null) {
                Debug.Log("[MoarScience!] There are no experiments.");
                return;
            }

            var experimentlist = GetExperimentList();
            for (int i = 0; i < experimentlist.Count; i++) {
                var currentExperiment = experimentlist[i];
                Debug.Log("[MoarScience!] Checking experiment: " + currentScienceSubject(currentExperiment.experiment).id);

                if (currentExperiment.GetData().Length > 0) {
                    Debug.Log("[MoarScience!] Skipping: Experiment already has data.");
                } else if (ActiveContainer() && ActiveContainer().HasData(newScienceData(currentExperiment))) {
                    Debug.Log("[MoarScience!] Skipping: We already have that data onboard.");
                } else if (!currentExperiment.experiment.IsUnlocked()) {
                    Debug.Log("[MoarScience!] Skipping: Experiment is not unlocked.");
                } else if (!currentExperiment.rerunnable && !IsScientistOnBoard()) {
                    Debug.Log("[MoarScience!] Skipping: Experiment is not repeatable.");
                } else if (!currentExperiment.experiment.IsAvailableWhile(currentSituation(), body)) {
                    Debug.Log("[MoarScience!] Skipping: Experiment is not available for this situation/atmosphere.");
                } else if (currentScienceValue(currentExperiment.experiment) < 0.1) {
                    Debug.Log("[MoarScience!] Skipping: No more science is available: ");
                } else {
                    Debug.Log("[MoarScience!] Running experiment: " + currentScienceSubject(currentExperiment.experiment).id);
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
            if ( ContainerList().Count == 0 ) {
                return null;
            } else {
                return ContainerList()[0];
            }
        }

        private bool HasContainer { get { return ContainerList().Count > 0; } }

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

        // every IScienceDataContainer (containers and experiments)
        List<IScienceDataContainer> ScienceModsAsInterface() {
            List<IScienceDataContainer> iscience = new List<IScienceDataContainer>();
            var containers = ContainerList();
            for (int i = 0; i < containers.Count; i++) {
                iscience.Add((IScienceDataContainer)containers[i]);
            }
            var experiments = GetExperimentList();
            for (int i = 0; i < experiments.Count; i++) {
                iscience.Add((IScienceDataContainer)experiments[i]);
            }
            return iscience;
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
                return GameDatabase.Instance.GetTexture("MoarScience/Icons/FS_active", false);
            else
                return GameDatabase.Instance.GetTexture("MoarScience/Icons/FS_inactive", false);
        }
    }
}
