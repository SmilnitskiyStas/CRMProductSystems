import { ScrollView, Text, TouchableOpacity } from 'react-native';
import type { LoyaltyMembershipSummary } from '../types';

interface Props {
  memberships: LoyaltyMembershipSummary[];
  selectedTenantId: string | null;
  onSelect: (tenantId: string) => void;
}

/** Horizontal chip selector — a consumer can hold memberships at several tenants ("wallet"). */
export function MembershipSelector({ memberships, selectedTenantId, onSelect }: Props) {
  if (memberships.length <= 1) return null;

  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      className="px-5 mb-3"
      contentContainerStyle={{ gap: 8 }}
    >
      {memberships.map((m) => {
        const selected = m.tenantId === selectedTenantId;
        return (
          <TouchableOpacity
            key={m.membershipId}
            onPress={() => onSelect(m.tenantId)}
            className={`px-4 py-2 rounded-full border ${
              selected ? 'bg-primary-600 border-primary-600' : 'bg-white border-gray-200'
            }`}
          >
            <Text className={selected ? 'text-white font-semibold' : 'text-gray-700'}>
              {m.tenantName}
            </Text>
          </TouchableOpacity>
        );
      })}
    </ScrollView>
  );
}
