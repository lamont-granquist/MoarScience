using KSP.UI.Screens;
using System.Collections.Generic;
using System.Linq;
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
        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

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
            if (FSAppButton != null) ApplicationLauncher.Instance.RemoveModApplication(FSAppButton);
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
                        // if we are in a new state, we will check and run experiments
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

                ActiveContainer().StoreData(GetExperimentList().Cast<IScienceDataContainer>().ToList(), true); // this is what actually moves the data to the active container
                var containerstotransfer = ContainerList(); // a temporary list of our containers
                containerstotransfer.Remove(ActiveContainer()); // we need to remove the container we storing the data in because that would be wierd and buggy
                ActiveContainer().StoreData(containerstotransfer.Cast<IScienceDataContainer>().ToList(), true); // now we store all data from other containers
            }
        }

        void RunScience() {
            if (GetExperimentList() == null) {
                Debug.Log("[ForScience!] There are no experiments.");
            } else {
                foreach (ModuleScienceExperiment currentExperiment in GetExperimentList()) {
                    Debug.Log("[ForScience!] Checking experiment: " + currentScienceSubject(currentExperiment.experiment).id);

                    if (ActiveContainer() && ActiveContainer().HasData(newScienceData(currentExperiment))) {
                        Debug.Log("[ForScience!] Skipping: We already have that data onboard.");
                    }
                    else if (!surfaceSamplesUnlocked() && currentExperiment.experiment.id == "surfaceSample") {
                        Debug.Log("[ForScience!] Skipping: Surface Samples are not unlocked.");
                    }
                    else if (!currentExperiment.rerunnable && !IsScientistOnBoard()) {
                        Debug.Log("[ForScience!] Skipping: Experiment is not repeatable.");
                    }
                    else if (!currentExperiment.experiment.IsAvailableWhile(currentSituation(), currentBody())) {
                        Debug.Log("[ForScience!] Skipping: Experiment is not available for this situation/atmosphere.");
                    }
                    else if (currentScienceValue(currentExperiment) < 0.1) {
                        Debug.Log("[ForScience!] Skipping: No more science is available: ");
                    } else {
                        Debug.Log("[ForScience!] Running experiment: " + currentScienceSubject(currentExperiment.experiment).id);
                        DeployExperiment(currentExperiment);
                    }
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

        float currentScienceValue(ModuleScienceExperiment currentExperiment) // the ammount of science an experiment should return
        {
            return ResearchAndDevelopment.GetScienceValue(
                                    currentExperiment.experiment.baseValue * currentExperiment.experiment.dataScale,
                                    currentScienceSubject(currentExperiment.experiment));
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

        private Vessel vessel { get { return  FlightGlobals.ActiveVessel; } }

        CelestialBody currentBody() {
            return vessel.mainBody;
        }

        ExperimentSituations currentSituation() {
            return ScienceUtil.GetExperimentSituation(vessel);
        }

        string currentBiome() // some crazy nonsense to get the actual biome string
        {
            if (vessel != null)
                if (vessel.mainBody.BiomeMap != null)
                    return !string.IsNullOrEmpty(vessel.landedAt)
                                    ? Vessel.GetLandedAtString(vessel.landedAt)
                                    : ScienceUtil.GetExperimentBiome(vessel.mainBody,
                                                vessel.latitude, vessel.longitude);

            return string.Empty;
        }

        ScienceSubject currentScienceSubject(ScienceExperiment experiment)
        {
            string fixBiome = string.Empty; // some biomes don't have 4th string, so we just put an empty in to compare strings later
            if (experiment.BiomeIsRelevantWhile(currentSituation())) fixBiome = currentBiome();// for those that do, we add it to the string
            return ResearchAndDevelopment.GetExperimentSubject(experiment, currentSituation(), currentBody(), fixBiome);//ikr!, we pretty much did all the work already, jeez
        }

        // set the container to gather all science data inside, usualy this is the root command pod of the oldest vessel
        ModuleScienceContainer ActiveContainer() {
            return ContainerList().FirstOrDefault();
        }

        private bool HasContainer { get { return ContainerList().Count() > 1; } }

        List<ModuleScienceExperiment> GetExperimentList() // a list of all experiments
        {
            return vessel.FindPartModulesImplementing<ModuleScienceExperiment>();
        }

        List<ModuleScienceContainer> ContainerList() // a list of all science containers
        {
            return vessel.FindPartModulesImplementing<ModuleScienceContainer>(); // list of all experiments onboard
        }

        bool StatesHaveChanged() // Track our vessel state, it is used for thread control to know when to fire off new experiments since there is no event for this
        {
            if (vessel != stateVessel | currentSituation() != stateSituation | currentBody() != stateBody | currentBiome() != stateBiome)
            {
                stateVessel = vessel;
                stateBody = currentBody();
                stateSituation = currentSituation();
                stateBiome = currentBiome();
                stopwatch.Reset();
                stopwatch.Start();
                return true;
            }
            else return false;
        }

        void toggleCollection() // This is our main toggle for the logic and changes the icon between green and red versions on the bar when it does so.
        {
            autoTransfer = !autoTransfer;
            FSAppButton.SetTexture(getIconTexture(autoTransfer));
        }

        bool IsScientistOnBoard() // check if there is a scientist onboard so we can rerun things like goo or scijrs
        {
            foreach (ProtoCrewMember kerbal in vessel.GetVesselCrew())
            {
                if (kerbal.experienceTrait.Title == "Scientist") return true;
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
