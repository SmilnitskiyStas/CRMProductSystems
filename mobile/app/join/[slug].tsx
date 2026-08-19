import { Redirect, useLocalSearchParams } from 'expo-router';

export default function RetailerJoinDeepLink() {
  const params = useLocalSearchParams<{ slug?: string | string[] }>();
  const slug = Array.isArray(params.slug) ? params.slug[0] : params.slug;
  if (!slug) return <Redirect href="/(personal)" />;
  return (
    <Redirect
      href={{
        pathname: '/(personal)/retailer-onboarding',
        params: { code: `shelfguard://join/${slug}` },
      }}
    />
  );
}
