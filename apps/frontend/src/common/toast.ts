import { toast, type ExternalToast } from "sonner";

export const showToast = (severity: "success" | "info" | "warn" | "error", title: string, message?: string) => {
  const options: ExternalToast = {
    description: message,
    duration: 4000,
  };

  switch (severity) {
    case "success":
      toast.success(title, options);
      break;
    case "info":
      toast.info(title, options);
      break;
    case "warn":
      toast.warning(title, options);
      break;
    case "error":
      toast.error(title, options);
      break;
    default:
      toast(title, options);
      break;
  }
};
