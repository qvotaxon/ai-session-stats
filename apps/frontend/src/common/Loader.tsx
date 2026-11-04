const Loader = () => {
  return (
    <div data-testid="loader" className="absolute inset-0 grid h-full w-full place-items-center bg-background/50">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-primary border-t-transparent" />
    </div>
  );
};

export { Loader };
