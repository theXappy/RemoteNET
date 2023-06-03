using ScubaDiver.Demangle.Demangle.Core.Machine;

namespace ScubaDiver.Demangle.Demangle.Core.NativeInterface
{
    public class NativeMachineInstruction : MachineInstruction
    {
        public override int MnemonicAsInteger => throw new NotImplementedException();
        public override string MnemonicAsString => throw new NotImplementedException();

        protected override void DoRender(MachineInstructionRenderer renderer, MachineInstructionRendererOptions options)
        {
            base.DoRender(renderer, options);
        }
    }
}
