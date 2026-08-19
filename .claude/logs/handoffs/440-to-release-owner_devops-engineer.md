# TASK-440 handoff — release owner

Configuration and local validation are complete; see `mobile/RELEASE.md` and the TASK-440 log.

To continue:

1. Obtain approved app icon, adaptive icon, splash, and Android notification artwork and wire the
   source files into `app.json`.
2. Provision EAS project access, Apple distribution/App Store Connect credentials, and Android
   upload key/Google Play Console access without committing secrets.
3. Commit the tracked `.expo/README.md` deletion, then require Expo Doctor 21/21.
4. With explicit authorization, run preview builds for Android+iOS, verify both use the production
   API and install without Metro, then run production AAB/IPA builds.
5. Record build URLs/IDs, hashes, devices/OS versions, install/cold-start/update-channel smoke, and
   store metadata/privacy results before marking TASK-440 done.

Do not submit or publish merely because builds succeed; those remain separate authorized actions.
