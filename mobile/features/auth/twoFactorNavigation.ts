export interface TwoFactorCancellation {
  clearChallenge: () => void;
  resetVerification: () => void;
  returnToLogin: () => void;
}

export function cancelTwoFactorChallenge(actions: TwoFactorCancellation): true {
  actions.clearChallenge();
  actions.resetVerification();
  actions.returnToLogin();
  return true;
}
