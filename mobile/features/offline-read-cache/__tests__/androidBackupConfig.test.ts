/* eslint-disable @typescript-eslint/no-require-imports */
const {
  applyAndroidManifestBackupPolicy,
  DATA_EXTRACTION_RULES,
} = require('../../../plugins/withShelfGuardBackupProtection');
const appConfig = require('../../../app.json');
const packageManifest = require('../../../package.json');

describe('Android private-storage backup policy', () => {
  test('disables legacy backup and binds Android 12+ extraction exclusions', () => {
    const manifest = { manifest: { application: [{ $: {} }] } };
    applyAndroidManifestBackupPolicy(manifest);
    expect(manifest.manifest.application[0].$).toMatchObject({
      'android:allowBackup': 'false',
      'android:fullBackupContent': 'false',
      'android:dataExtractionRules': '@xml/shelfguard_data_extraction_rules',
    });
  });

  test('excludes every app-private domain from cloud backup and device transfer', () => {
    expect(DATA_EXTRACTION_RULES).toContain('<cloud-backup>');
    expect(DATA_EXTRACTION_RULES).toContain('<device-transfer>');
    for (const domain of ['root', 'file', 'database', 'sharedpref', 'external']) {
      expect(DATA_EXTRACTION_RULES.match(new RegExp(`domain="${domain}"`, 'g'))).toHaveLength(2);
    }
  });
});

describe('Expo native runtime configuration', () => {
  test('ships the SDK-aligned splash module required by the development launcher', () => {
    expect(packageManifest.dependencies['expo-splash-screen']).toBe('~56.0.14');
    expect(appConfig.expo.plugins).toContain('expo-splash-screen');
  });
});
