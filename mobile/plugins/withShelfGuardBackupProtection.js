const { withAndroidManifest, withDangerousMod } = require('expo/config-plugins');
const fs = require('fs');
const path = require('path');

const DATA_EXTRACTION_RULES = `<?xml version="1.0" encoding="utf-8"?>
<data-extraction-rules>
  <cloud-backup>
    <exclude domain="root" path="." />
    <exclude domain="file" path="." />
    <exclude domain="database" path="." />
    <exclude domain="sharedpref" path="." />
    <exclude domain="external" path="." />
  </cloud-backup>
  <device-transfer>
    <exclude domain="root" path="." />
    <exclude domain="file" path="." />
    <exclude domain="database" path="." />
    <exclude domain="sharedpref" path="." />
    <exclude domain="external" path="." />
  </device-transfer>
</data-extraction-rules>
`;

function applyAndroidManifestBackupPolicy(manifest) {
  const application = manifest?.manifest?.application?.[0];
  if (!application?.$) throw new Error('ShelfGuard backup policy requires a main Android application');
  application.$['android:allowBackup'] = 'false';
  application.$['android:fullBackupContent'] = 'false';
  application.$['android:dataExtractionRules'] = '@xml/shelfguard_data_extraction_rules';
  return manifest;
}

function withShelfGuardBackupProtection(config) {
  config = withAndroidManifest(config, (configWithManifest) => {
    configWithManifest.modResults = applyAndroidManifestBackupPolicy(configWithManifest.modResults);
    return configWithManifest;
  });
  return withDangerousMod(config, ['android', async (configWithFiles) => {
    const xmlDir = path.join(configWithFiles.modRequest.platformProjectRoot, 'app', 'src', 'main', 'res', 'xml');
    fs.mkdirSync(xmlDir, { recursive: true });
    fs.writeFileSync(path.join(xmlDir, 'shelfguard_data_extraction_rules.xml'), DATA_EXTRACTION_RULES);
    return configWithFiles;
  }]);
}

module.exports = withShelfGuardBackupProtection;
module.exports.applyAndroidManifestBackupPolicy = applyAndroidManifestBackupPolicy;
module.exports.DATA_EXTRACTION_RULES = DATA_EXTRACTION_RULES;
