# TASK-463 mobile runtime fix → QA

The SDK56 missing-class defect is fixed by the direct aligned `expo-splash-screen~56.0.14`
dependency/plugin. A fresh APK was built and replacement-installed with app data preserved; native
cold launch no longer emits the prior splash/manifest/fatal signatures.

Resume with one owned Metro session and verify the current-source manifest plus first JS screen.
Only after that smoke passes, continue cache/process-death/reconnect/owner-switch/POS guards from the
existing TASK-463 QA matrix. Do not repeat build recovery or clear application data. iOS remains a
separate build/device gate.
