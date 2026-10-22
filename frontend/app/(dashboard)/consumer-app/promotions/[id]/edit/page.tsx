"use client";
import { useParams } from "next/navigation";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { PromotionCampaignForm } from "@/features/consumer-app/components/PromotionCampaignForm";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { PageContainer } from "@/components/ui/PageContainer";
export default function EditPromotionCampaignPage(){const {id}=useParams<{id:string}>();const {data:me}=useMe();const roleAccess=me?hasRole(me.role,AT_LEAST_ENTERPRISE_ADMIN):null;if(roleAccess===false)return <AccessDenied title="Акції"/>;if(roleAccess===null)return null;return <PageContainer><PromotionCampaignForm campaignId={id}/></PageContainer>}
