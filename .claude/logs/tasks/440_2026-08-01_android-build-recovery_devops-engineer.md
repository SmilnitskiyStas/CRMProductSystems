# TASK-440 / TASK-463 — Android local build recovery

**Date:** 2026-08-01  
**Agent:** devops-engineer  
**Status:** android_build_recovered / installed_pending_QA

## Actions

1. Read the TASK-463 physical QA report and mobile/devops handoff.
2. Resolved every cleanup target under
   `C:\Users\stass\source\CRMProductSystems\mobile` before deletion.
3. Ran `android\gradlew.bat --stop`; one Gradle daemon stopped.
4. Removed only derived directories:
   - `mobile/android/app/build`
   - `mobile/android/.gradle`
   - `mobile/node_modules/react-native-worklets/android/.cxx`
   - `mobile/node_modules/react-native-worklets/android/build`
5. Windows sandbox removal was denied for generated native `.so` files. Elevated deletion was used
   only for the two exact remaining build directories. No `node_modules` package source, package
   manifest, application source, app data, or broad workspace path was removed.
6. Started a clean build from `mobile/android` with PowerShell stop-parsing:

```powershell
.\gradlew.bat --% assembleDebug --no-daemon --console=plain --stacktrace -Dorg.gradle.jvmargs=-Xmx4096m
```

Two earlier invocations did not reach compilation because PowerShell transformed the JVM property
into a task name; those quoting errors are not application failures.

## Result

The corrected build configured Expo SDK 56, compile/target SDK 36, NDK 27.1, Kotlin 2.1.20 and all
native modules, then continued full CMake/native compilation. Neither the locked
`mergeDebugResources/.../values.xml` nor missing react-native-worklets CMake reply error recurred.
After approximately nine minutes the build still had not returned output or produced
`app/build/outputs/apk/debug/*.apk`, so it was terminated to keep the recovery bounded. The
single-use Gradle daemon and the two Java compiler workers started during this build were stopped.
The captured daemon tail contains only third-party Kotlin deprecation/type warnings followed by
`Daemon is stopping immediately stop command received` and normal/interrupted shutdown messages;
there is no new Gradle task exception, resource-lock failure, or missing CMake reply. Therefore the
final condition is an operator-enforced build-window termination during compilation, not a newly
diagnosed source failure.

No APK timestamp/hash, native backup-policy inspection, ADB install, or package metadata result is
claimed because no fresh artifact exists. No Metro or device QA was launched.

## Next action

On an otherwise idle machine, run the exact corrected command above with a longer build window. Do
not clean again unless the original generated lock/CMake-reply errors return. On success:

1. Record APK path, timestamp, size, SHA-256.
2. Inspect merged manifest/resources for TASK-463 backup exclusions and permission policy.
3. If ADB device is connected, use `adb install --no-streaming -r <apk>` to preserve app data.
4. Verify package/version metadata without launching Metro or QA, then hand back to TASK-463 QA.

## Final incremental build and install

An explicitly authorized final incremental invocation of the exact corrected command completed
successfully in 497 seconds (exit code 0), without another clean or deletion.

- APK: `mobile/android/app/build/outputs/apk/debug/app-debug.apk`
- Modified: `2026-08-01T20:46:29.0039209+03:00`
- Size: `262092673` bytes
- SHA-256: `3FC63D56A5F3BC32C012E3CD9EB0E15BE9D4B54DDEEA8C1ABB6F8508D1498058`
- Merged/packaged manifest: `CAMERA` present, `RECORD_AUDIO` absent, `allowBackup=false`,
  `fullBackupContent=false`, `dataExtractionRules=@xml/shelfguard_data_extraction_rules`.
- Packaged extraction rules exclude root/file/database/sharedpref/external domains for both cloud
  backup and device transfer.
- `adb install --no-streaming -r`: `Success`; replacement preserved application data.
- Installed package: `com.shelfguard.mobile`, version `1.0.0` / code `1`, target SDK `36`.
- `firstInstallTime=2026-07-29 20:24:09`; `lastUpdateTime=2026-08-01 20:47:06`, proving replacement
  rather than clear/reinstall.

No Metro session or QA flow was launched. Android build/runtime blocker is cleared and ownership
returns to TASK-463 QA. iOS acceptance remains separately blocked.
